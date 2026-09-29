namespace Kashif;

/// <summary>حقوق البرنامج: المصمم والمطوّر ووسائل التواصل (تظهر أسفل القائمة الجانبية وفي «حول كاشف»)</summary>
public static class Credits
{
    public const string Author = "يوسف أحمد";
    public const string Telegram = "YsYsD";
    public const string Instagram = "Gxp6";
    public const string Phone = "07764455011";

    public static string Short => "تصميم وتطوير: " + Author;
    public static string Contacts => $"Telegram: {Telegram}  ·  Instagram: {Instagram}";

    public static string Full =>
        $"كاشف — تحليل بانك الآيفون وتشخيص أعطاله\nالإصدار {Application.ProductVersion.Split('+')[0]}\n\n" +
        $"{Short}\nTelegram: {Telegram}\nInstagram: {Instagram}\nالهاتف: {Phone}\n\nجميع الحقوق محفوظة © {Author}";

    public static void ShowAbout() => Dialogs.Message(Full, "حول كاشف", Tone.Info);
}
