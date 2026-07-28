using System; using Avalonia.Input.Platform; class Program { static void Main() { var type = typeof(IClipboard); foreach(var m in type.GetMethods()) Console.WriteLine(m.Name); } }
