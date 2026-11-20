namespace HyprStart.Core.Input;

public enum HookDecision
{
    /// <summary>Событие проходит дальше, система ведёт себя как обычно.</summary>
    Pass,

    /// <summary>Событие съедается, оверлей нужно показать/переключить.</summary>
    SuppressAndOpen,
}

/// <summary>
/// Автомат распознавания «тап Win» vs «сочетание с Win».
///
/// Логика: keydown Win пропускаем всегда (иначе Win+E/Win+D/Win+R перестанут работать,
/// потому что система не увидит модификатор). Съедаем только keyup — и только если
/// за время удержания не было ни одной другой клавиши и не была нажата мышь.
/// Именно так нативное меню «Пуск» и перестаёт открываться по тапу.
///
/// Класс чистый (без Win32), поэтому тестируется без хука. Никаких аллокаций в Process —
/// этот код вызывается из низкоуровневого колбэка.
/// </summary>
public sealed class WinKeyStateMachine
{
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;

    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    private const long WinDownStaleMs = 5000;

    private readonly Func<bool>? _pointerDown;

    private bool _winDown;
    private bool _comboUsed;
    private long _winDownAt;

    /// <param name="pointerDown">Проверка «нажата ли кнопка мыши» — чтобы Win+клик не считался тапом.</param>
    public WinKeyStateMachine(Func<bool>? pointerDown = null) => _pointerDown = pointerDown;

    public bool IsWinDown => _winDown;
    public bool ComboUsed => _comboUsed;

    /// <summary>Сколько тапов было съедено — для диагностики.</summary>
    public int SuppressedTaps { get; private set; }

    public static bool IsWinKey(int vk) => vk is VkLWin or VkRWin;

    public HookDecision Process(int message, int vk, long nowMs = 0)
    {
        if (nowMs == 0) nowMs = Environment.TickCount64;

        switch (message)
        {
            case WmKeyDown:
            case WmSysKeyDown:
                if (IsWinKey(vk))
                {
                    if (!_winDown)
                    {
                        _winDown = true;
                        _comboUsed = false;
                        _winDownAt = nowMs;
                    }
                }
                else if (_winDown)
                {
                    _comboUsed = true;
                }
                return HookDecision.Pass;

            case WmKeyUp:
            case WmSysKeyUp:
                if (IsWinKey(vk))
                {
                    if (!_winDown) return HookDecision.Pass;

                    // Самолечение: если keyup потерялся (съел другой хук/игра),
                    // не залипаем в состоянии «Win зажат».
                    bool stale = nowMs - _winDownAt > WinDownStaleMs;
                    _winDown = false;
                    bool tap = !_comboUsed && !stale && !PointerIsDown();
                    _comboUsed = false;

                    if (tap)
                    {
                        SuppressedTaps++;
                        return HookDecision.SuppressAndOpen;
                    }
                    return HookDecision.Pass;
                }

                if (_winDown) _comboUsed = true;
                return HookDecision.Pass;

            default:
                return HookDecision.Pass;
        }
    }

    public void Reset()
    {
        _winDown = false;
        _comboUsed = false;
    }

    private bool PointerIsDown() => _pointerDown?.Invoke() ?? false;
}
