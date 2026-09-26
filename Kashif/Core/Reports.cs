using System.Data;

namespace Kashif;

/// <summary>استعلامات التقارير (مفصولة عن الشاشة لتُستخدم في الطباعة والتصدير)</summary>
public static class Reports
{
    public static readonly string[] Kinds = { "الأعطال الأكثر تكرارًا", "حسب نوع البانك", "حسب الجهاز", "سجل العمليات" };

    public static string Subtitle(string kind) => kind switch
    {
        "الأعطال الأكثر تكرارًا" => "القطعة الأرجح في كل فحص — ما يستحق التخزين أولًا",
        "حسب نوع البانك" => "حساس مفقود، SMC، مراقب النظام، التخزين ...",
        "حسب الجهاز" => "الموديلات الأكثر وصولًا بالبانك",
        _ => "من غيّر ماذا ومتى",
    };

    public static DataTable Run(string kind, DateTime from, DateTime to)
    {
        string d1 = from.ToString(Ui.DFmt), d2 = to.ToString(Ui.DFmt) + " 23:59:59";
        switch (kind)
        {
            case "الأعطال الأكثر تكرارًا":
                return Db.Query(@"SELECT 0 AS id, top_part AS [القطعة الأرجح], COUNT(*) AS [عدد الفحوصات],
                        SUM(CASE WHEN confidence='عالية' THEN 1 ELSE 0 END) AS [بثقة عالية],
                        GROUP_CONCAT(DISTINCT device) AS [الأجهزة], MAX(date) AS [آخر فحص]
                    FROM analyses WHERE IFNULL(top_part,'')<>'' AND date BETWEEN @p0 AND @p1
                    GROUP BY top_part ORDER BY [عدد الفحوصات] DESC", d1, d2);
            case "حسب نوع البانك":
                return Db.Query(@"SELECT 0 AS id, kind AS [النوع], COUNT(*) AS [عدد الفحوصات], GROUP_CONCAT(DISTINCT top_part) AS [القطع الأرجح], MAX(date) AS [آخر فحص]
                    FROM analyses WHERE date BETWEEN @p0 AND @p1 GROUP BY kind ORDER BY [عدد الفحوصات] DESC", d1, d2);
            case "حسب الجهاز":
                return Db.Query(@"SELECT 0 AS id, device AS [الجهاز], COUNT(*) AS [عدد الفحوصات], GROUP_CONCAT(DISTINCT top_part) AS [القطع الأرجح], MAX(date) AS [آخر فحص]
                    FROM analyses WHERE IFNULL(device,'')<>'' AND date BETWEEN @p0 AND @p1 GROUP BY device ORDER BY [عدد الفحوصات] DESC", d1, d2);
            default: // سجل العمليات
                return Db.Query(@"SELECT a.id, a.date AS [التاريخ], IFNULL(u.full_name,u.username) AS [المستخدم], a.action AS [العملية], a.details AS [التفاصيل]
                    FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.date BETWEEN @p0 AND @p1 ORDER BY a.id DESC", d1, d2);
        }
    }
}
