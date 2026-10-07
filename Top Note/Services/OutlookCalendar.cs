using System.Globalization;
using System.Runtime.InteropServices;

namespace Top_Note.Services
{
    public record CalendarEvent(DateTime Start, DateTime End, string Subject, bool AllDay);

    // Reads the default calendar of classic Outlook through COM. Late-bound so Top Note
    // still builds and runs on PCs without Outlook (it just gets no meetings).
    public static class OutlookCalendar
    {
        private const int OlFolderCalendar = 9;
        private const int OlMeetingCanceled = 5;
        private const int OlMeetingReceivedAndCanceled = 7;

        public static bool IsInstalled => Type.GetTypeFromProgID("Outlook.Application") != null;

        // Null when Outlook can't be reached; an empty list means a free day.
        public static List<CalendarEvent>? GetEvents(DateTime day)
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null) return null;

            dynamic? outlook = null, calendar = null, items = null, found = null;
            try
            {
                outlook = Activator.CreateInstance(type);
                if (outlook == null) return null;
                calendar = outlook.GetNamespace("MAPI").GetDefaultFolder(OlFolderCalendar);
                items = calendar.Items;
                // Both are needed (in this order) for recurring meetings to appear as single occurrences.
                items.IncludeRecurrences = true;
                items.Sort("[Start]");

                // Outlook parses filter dates in the user's regional format.
                string from = day.Date.ToString("g", CultureInfo.CurrentCulture);
                string to = day.Date.AddDays(1).ToString("g", CultureInfo.CurrentCulture);
                found = items.Restrict($"[Start] < '{to}' AND [End] > '{from}'");

                var events = new List<CalendarEvent>();
                foreach (dynamic item in found)
                {
                    try
                    {
                        int status = item.MeetingStatus;
                        if (status is OlMeetingCanceled or OlMeetingReceivedAndCanceled) continue;
                        DateTime start = item.Start, end = item.End;
                        // Restrict can be loose with recurrences; keep only what really touches the day.
                        if (start >= day.Date.AddDays(1) || end <= day.Date) continue;
                        events.Add(new CalendarEvent(start, end, ((string?)item.Subject)?.Trim() ?? "", (bool)item.AllDayEvent));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
                return events.OrderBy(e => !e.AllDay).ThenBy(e => e.Start).ToList();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                foreach (var com in new object?[] { found, items, calendar, outlook })
                {
                    if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
                }
            }
        }
    }
}
