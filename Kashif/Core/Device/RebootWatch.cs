namespace Kashif.Device;

/// <summary>
/// اختبار العزل: بعد فصل قطعة يُشغَّل الجهاز ويُنتظر. إن أعاد التشغيل ينقطع عن الكيبل لحظة (يختفي من usbmuxd) فيُسجَّل الوقت؛
/// وإن بقي يعمل حتى المهلة فالقطعة المفصولة هي السبب على الأرجح. بلا كيبل: يحكم الفني بالأزرار.
/// </summary>
public sealed class RebootWatch
{
    public enum State { Idle, Running, Rebooted, Passed, Stopped }

    readonly Func<IReadOnlyCollection<string>> connected;
    DateTime start;
    public string Udid { get; private set; }
    public TimeSpan Limit { get; private set; }
    public State Current { get; private set; } = State.Idle;
    public TimeSpan Result { get; private set; }
    /// <summary>هل يُراقب الجهاز بالكيبل (وُجد موصولًا عند البدء)</summary>
    public bool Watching => Udid != null;

    /// <param name="connected">أرقام UDID الموصولة الآن (null أو خطأ = لا خدمة Apple: بلا مراقبة)</param>
    public RebootWatch(Func<IReadOnlyCollection<string>> connected) { this.connected = connected; }

    /// <summary>المهلة: ضعف المدة من الإقلاع إلى الانهيار في السجل، بين 4 و 15 دقيقة؛ 6 دقائق إن لم تُعرف</summary>
    public static TimeSpan LimitFor(double? uptimeSeconds) => PanicAnalyzer.IsolationWait(uptimeSeconds);

    public void Start(DateTime now, TimeSpan limit)
    {
        start = now;
        Limit = limit;
        Result = TimeSpan.Zero;
        Current = State.Running;
        Udid = Safe()?.FirstOrDefault();
    }

    public TimeSpan Elapsed(DateTime now) => Current == State.Running ? now - start : Result;

    /// <summary>كل ثانية تقريبًا: يتحقق من بقاء الجهاز موصولًا ومن المهلة</summary>
    public State Tick(DateTime now)
    {
        if (Current != State.Running) return Current;
        if (Udid != null && Safe() is { } list && !list.Contains(Udid)) return Finish(State.Rebooted, now);
        if (now - start >= Limit) return Finish(State.Passed, now);
        return Current;
    }

    /// <summary>حكم الفني يدويًا (بلا كيبل، أو لإنهاء الاختبار مبكرًا)</summary>
    public State Mark(bool rebooted, DateTime now) => Current == State.Running ? Finish(rebooted ? State.Rebooted : State.Passed, now) : Current;

    public void Stop(DateTime now) { if (Current == State.Running) Finish(State.Stopped, now); }

    State Finish(State s, DateTime now)
    {
        Result = now - start;
        Current = s;
        return s;
    }

    IReadOnlyCollection<string> Safe()
    {
        try { return connected?.Invoke(); } catch { return null; }
    }

    public static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}
