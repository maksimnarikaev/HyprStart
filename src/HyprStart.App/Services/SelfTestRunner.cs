using HyprStart.App.Interop;

namespace HyprStart.App.Services;

/// <summary>
/// Автопроверка без человека: эмулируем Alt+Space и обычный Space, смотрим,
/// что оверлей переключается только по хоткею.
/// Хук клавиатуры не используется — тест физически не может «залипить» модификаторы.
/// </summary>
internal static class SelfTestRunner
{
    public static async Task<int> RunAsync(OverlayController overlay, bool hotkeyRegistered, bool keyboardHookActive)
    {
        int failures = 0;

        AppLog.Write("selftest: начало");
        await Task.Delay(600);

        failures += await CheckAsync("хоткей зарегистрирован", () => Task.FromResult(hotkeyRegistered));
        failures += await CheckAsync("перехват Win отключён (безопасный режим)", () => Task.FromResult(!keyboardHookActive));

        failures += await CheckAsync("Alt+Space открывает оверлей", async () =>
        {
            await SendAsync(SendAltSpace);
            await Task.Delay(450);
            return overlay.IsVisible;
        });

        failures += await CheckAsync("повторный Alt+Space закрывает оверлей", async () =>
        {
            await SendAsync(SendAltSpace);
            await Task.Delay(450);
            return !overlay.IsVisible;
        });

        failures += await CheckAsync("обычный Space не открывает оверлей", async () =>
        {
            await SendAsync(SendSpace);
            await Task.Delay(400);
            return !overlay.IsVisible;
        });

        failures += await CheckAsync("оверлей закрыт в конце теста", () => Task.FromResult(!overlay.IsVisible));

        Report(failures);
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> CheckAsync(string name, Func<Task<bool>> check)
    {
        bool ok;
        try
        {
            ok = await check();
        }
        catch (Exception ex)
        {
            AppLog.Error($"selftest: '{name}' упало", ex);
            ok = false;
        }

        AppLog.Write($"selftest: [{(ok ? "OK  " : "FAIL")}] {name}");
        return ok ? 0 : 1;
    }

    private static void Report(int failures)
    {
        var summary = failures == 0
            ? "selftest: ВСЁ ОК"
            : $"selftest: провалов {failures}";
        AppLog.Write(summary);
        Console.WriteLine();
        Console.WriteLine(summary);
    }

    /// <summary>Ввод эмулируем на пуле потоков, чтобы не блокировать диспетчер.</summary>
    private static Task SendAsync(Action send) => Task.Run(async () =>
    {
        send();
        await Task.Delay(60);
    });

    private static void SendAltSpace()
    {
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        NativeMethods.keybd_event(NativeMethods.VK_SPACE, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        NativeMethods.keybd_event(NativeMethods.VK_SPACE, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(30);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendSpace()
    {
        NativeMethods.keybd_event(NativeMethods.VK_SPACE, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        NativeMethods.keybd_event(NativeMethods.VK_SPACE, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
