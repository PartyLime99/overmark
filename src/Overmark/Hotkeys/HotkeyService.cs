using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Hotkeys;

/// <summary>
/// Global hotkeys, caught two ways on a dedicated thread:
/// <list type="bullet">
/// <item>A low-level keyboard hook. It sees keys before other apps' hotkeys (ShareX, OneDrive, Snipping Tool),
/// so Overmark gets Print Screen even if another app has claimed it, and it swallows the key.</item>
/// <item>RegisterHotKey as a backup, because Windows doesn't call a normal app's hook while an admin window is
/// focused. Registration fails if another app already owns the key; the hook still covers everything else.</item>
/// </list>
/// The hook has its own thread because every keystroke on the PC waits for it, so it must never queue behind UI work.
/// </summary>
internal sealed class HotkeyService : IHotkeyRecorder, IDisposable
{
    private const uint WM_RUN_WORK = WM_APP + 1;

    // An unassigned virtual key. Tapped after swallowing an Alt or Win combo; otherwise releasing Alt/Win
    // on its own would open the app's menu bar or the Start menu.
    private const ushort MaskVirtualKey = 0xE8;

    // A second trigger of the same action this soon after the first is ignored, in case the hook and
    // RegisterHotKey both report one key press.
    private const long DebounceMilliseconds = 300;

    private readonly Dispatcher _dispatcher;
    private readonly HotkeyMatcher _matcher = new();
    private readonly object _matcherLock = new();
    private readonly LowLevelKeyboardProc _hookProc; // must stay referenced while Windows can call it
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();

    // Only touched on the hook thread.
    private readonly Dictionary<int, HotkeyAction> _registeredIds = [];
    private readonly Dictionary<HotkeyAction, long> _lastFired = [];

    private uint _threadId;
    private IntPtr _hook;

    // While set, key presses go here (for the settings window) instead of triggering hotkeys. Guarded by _matcherLock.
    private Action<Hotkey>? _recorder;

    public HotkeyService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _hookProc = HookCallback;
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "Overmark hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Raised on the UI thread.</summary>
    public event Action<HotkeyAction>? Triggered;

    public bool HookActive => _hook != IntPtr.Zero;

    /// <summary>
    /// Replaces the hotkeys. Returns the actions RegisterHotKey also accepted, i.e. the ones that keep working
    /// while an admin window is focused.
    /// </summary>
    public IReadOnlySet<HotkeyAction> Apply(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        lock (_matcherLock)
        {
            _matcher.SetBindings(bindings);
        }

        var registered = new TaskCompletionSource<IReadOnlySet<HotkeyAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => registered.SetResult(RegisterAll(bindings)));
        return registered.Task.Wait(TimeSpan.FromSeconds(2)) ? registered.Task.Result : new HashSet<HotkeyAction>();
    }

    /// <summary>
    /// Sends the next key presses (with their modifiers) to <paramref name="recorder"/> on the UI thread instead of
    /// acting on them, and swallows them so no other app sees them. Modifier keys pass through. Call
    /// <see cref="StopRecording"/> as soon as possible: until then the keyboard doesn't reach any app.
    /// </summary>
    public void StartRecording(Action<Hotkey> recorder)
    {
        lock (_matcherLock)
        {
            _recorder = recorder;
        }
    }

    public void StopRecording()
    {
        lock (_matcherLock)
        {
            _recorder = null;
        }
    }

    public void Dispose()
    {
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // creates this thread's message queue
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        var hookError = Marshal.GetLastPInvokeError();
        _ready.Set();

        if (_hook == IntPtr.Zero)
        {
            Log.Error($"Keyboard hook could not be installed (Win32 error {hookError})");
        }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY && _registeredIds.TryGetValue((int)msg.wParam, out var action))
            {
                Fire(action);
            }
            else if (msg.message == WM_RUN_WORK)
            {
                while (_work.TryDequeue(out var work))
                {
                    work();
                }
            }
        }

        UnregisterAll();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (key.vkCode != MaskVirtualKey && Decide((int)wParam, (int)key.vkCode).Swallow)
            {
                return 1;
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private KeyDecision Decide(int message, int virtualKey)
    {
        var modifiers = CurrentModifiers();
        KeyDecision decision;
        lock (_matcherLock)
        {
            if (_recorder is { } recorder)
            {
                return Record(recorder, message, virtualKey, modifiers);
            }

            decision = message switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => _matcher.KeyDown(virtualKey, modifiers),
                WM_KEYUP or WM_SYSKEYUP => _matcher.KeyUp(virtualKey, modifiers),
                _ => KeyDecision.PassThrough,
            };
        }

        if (decision.Fire is { } action)
        {
            if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            {
                Post(SendMaskKey); // after the hook returns, never from inside it
            }

            Fire(action);
        }

        return decision;
    }

    private KeyDecision Record(Action<Hotkey> recorder, int message, int virtualKey, ModifierKeys modifiers)
    {
        if (IsModifierKey(virtualKey))
        {
            return KeyDecision.PassThrough;
        }

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            {
                Post(SendMaskKey);
            }

            var hotkey = new Hotkey(modifiers, virtualKey);
            _dispatcher.InvokeAsync(() => recorder(hotkey));
        }

        return new KeyDecision(true, null);
    }

    private static bool IsModifierKey(int virtualKey) =>
        virtualKey is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN or >= 0xA0 and <= 0xA5;

    private void Fire(HotkeyAction action)
    {
        var now = Environment.TickCount64;
        if (_lastFired.TryGetValue(action, out var last) && now - last < DebounceMilliseconds)
        {
            return;
        }

        _lastFired[action] = now;
        _dispatcher.InvokeAsync(() => Triggered?.Invoke(action));
    }

    private HashSet<HotkeyAction> RegisterAll(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        UnregisterAll();
        var id = 1;
        foreach (var (action, hotkey) in bindings)
        {
            if (RegisterHotKey(IntPtr.Zero, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.VirtualKey))
            {
                _registeredIds[id] = action;
            }

            id++;
        }

        return [.. _registeredIds.Values];
    }

    private void UnregisterAll()
    {
        foreach (var id in _registeredIds.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, id);
        }

        _registeredIds.Clear();
    }

    private void Post(Action work)
    {
        _work.Enqueue(work);
        PostThreadMessage(_threadId, WM_RUN_WORK, IntPtr.Zero, IntPtr.Zero);
    }

    private static ModifierKeys CurrentModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (IsDown(VK_CONTROL)) modifiers |= ModifierKeys.Control;
        if (IsDown(VK_SHIFT)) modifiers |= ModifierKeys.Shift;
        if (IsDown(VK_MENU)) modifiers |= ModifierKeys.Alt;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) modifiers |= ModifierKeys.Windows;
        return modifiers;

        static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void SendMaskKey()
    {
        INPUT[] inputs =
        [
            new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = MaskVirtualKey } } },
            new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = MaskVirtualKey, dwFlags = KEYEVENTF_KEYUP } } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
