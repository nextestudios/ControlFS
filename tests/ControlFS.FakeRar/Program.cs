using System.Text;

// Uso: dotnet ControlFS.FakeRar.dll --mode <ok|warn|fail|hang|empty> a <opções> <arquivo> @<lista>
// O "arquivo" gerado é a assinatura RAR5 seguida de um relato do que foi recebido (não é um RAR de verdade).
var args2 = args.ToList();
var mode = "ok";
if (args2.Count >= 2 && args2[0] == "--mode") { mode = args2[1]; args2.RemoveRange(0, 2); }

string? archive = null, listFile = null;
foreach (var a in args2.Skip(1))
{
    if (a.StartsWith('@')) listFile = a[1..];
    else if (!a.StartsWith('-') && archive is null) archive = a;
}
if (archive is null || listFile is null) { Console.Error.WriteLine("uso inválido"); return 7; }

var report = new StringBuilder();
report.AppendLine("CWD=" + Directory.GetCurrentDirectory());
foreach (var a in args2) report.AppendLine("ARG=" + a);
report.AppendLine("LISTEXISTS=" + File.Exists(listFile));
foreach (var line in ReadList(listFile)) report.AppendLine("LIST=" + line);

byte[] signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
void Write(bool valid)
{
    using var f = new FileStream(archive, FileMode.CreateNew, FileAccess.Write);
    if (valid) f.Write(signature);
    f.Write(Encoding.UTF8.GetBytes(report.ToString()));
}

switch (mode)
{
    case "ok": Write(true); return 0;
    case "warn": Write(true); Console.Error.WriteLine("WARNING: Cannot open x.txt"); return 1;
    case "fail": Write(false); Console.Error.WriteLine("ERROR: disk write error\u0007"); return 5;
    case "empty": return 0; // diz que deu certo mas não cria nada
    case "hang":
        Write(true);
        Thread.Sleep(Timeout.Infinite);
        return 0;
    default: return 7;
}

static IEnumerable<string> ReadList(string path)
{
    var bytes = File.ReadAllBytes(path);
    var text = bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.UTF8.GetString(bytes);
    return text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}
