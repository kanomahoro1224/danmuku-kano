using System; using System.Reflection; class P { static void Main() { var t = typeof(Windows.UI.Notifications.Notification); foreach(var p in t.GetProperties()) Console.WriteLine(p.Name); } }
