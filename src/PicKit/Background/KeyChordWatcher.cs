namespace PicKit.Background;

/// <summary>
/// Spots Ctrl+Alt+S in the raw keyboard stream. Windows' hotkey system comes after other apps'
/// keyboard hooks, which can swallow a real key press before it gets there. Raw input doesn't
/// pass through them. Only this one combination is looked for, and nothing typed is ever kept.
/// </summary>
public sealed class KeyChordWatcher
{
    private const int ControlKey = 0x11;
    private const int LeftControlKey = 0xA2;
    private const int RightControlKey = 0xA3;
    private const int AltKey = 0x12;
    private const int LeftAltKey = 0xA4;
    private const int RightAltKey = 0xA5;
    private const int ShiftKey = 0x10;
    private const int LeftShiftKey = 0xA0;
    private const int RightShiftKey = 0xA1;
    private const int LeftWindowsKey = 0x5B;
    private const int RightWindowsKey = 0x5C;
    private const int LetterKey = 'S';

    /// <summary>
    /// A key's release is never seen if it happens on the sign-in screen, which would leave it
    /// looking held forever. Held keys repeat, so anything this quiet is treated as released.
    /// </summary>
    private const long HeldKeyTimeoutMs = 5000;

    private long? _controlDownAt;
    private long? _altDownAt;
    private bool _isShiftDown;
    private bool _isWindowsDown;
    private bool _isLetterDown;

    /// <summary>Takes one key event, and returns true if it completed the combination.</summary>
    public bool OnKey(int virtualKey, bool isDown, long now)
    {
        switch (virtualKey)
        {
            case ControlKey or LeftControlKey or RightControlKey:
                _controlDownAt = isDown ? now : null;
                RefreshHeldKeys(now);
                return false;
            case AltKey or LeftAltKey or RightAltKey:
                _altDownAt = isDown ? now : null;
                RefreshHeldKeys(now);
                return false;
            case ShiftKey or LeftShiftKey or RightShiftKey:
                _isShiftDown = isDown;
                return false;
            case LeftWindowsKey or RightWindowsKey:
                _isWindowsDown = isDown;
                return false;
            case LetterKey:
                var isNewPress = isDown && !_isLetterDown;
                _isLetterDown = isDown;
                return isNewPress && IsHeld(_controlDownAt, now) && IsHeld(_altDownAt, now) && !_isShiftDown && !_isWindowsDown;
            default:
                return false;
        }
    }

    private static bool IsHeld(long? downAt, long now) => downAt is { } time && now - time < HeldKeyTimeoutMs;

    /// <summary>Only the last key pressed repeats, so its repeats speak for the other one too.</summary>
    private void RefreshHeldKeys(long now)
    {
        if (IsHeld(_controlDownAt, now) && IsHeld(_altDownAt, now))
        {
            _controlDownAt = now;
            _altDownAt = now;
        }
    }
}

/// <summary>Stops one key press that arrives by two routes from starting two snips.</summary>
public sealed class TriggerGate
{
    private const long MinimumGapMs = 500;

    private long? _lastPass;

    public bool TryPass(long now)
    {
        if (_lastPass is { } last && now - last < MinimumGapMs)
        {
            return false;
        }

        _lastPass = now;
        return true;
    }
}
