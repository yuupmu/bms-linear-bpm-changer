using System.Text;

namespace BmsLinearBpmChanger.Core;

public static class BmsWriter
{
    public static byte[] Build(BmsDocument document, PreparedConversion prepared)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(prepared);
        if (!prepared.CanConvert)
            throw new InvalidOperationException("충돌 또는 입력 오류를 해결한 뒤 변환하세요.");

        var insertion = prepared.Definitions.Select(definition => $"#BPM{definition.Id} {definition.Text}")
            .Concat(prepared.ChannelLines)
            .ToList();
        if (insertion.Count == 0)
            throw new InvalidOperationException("삽입할 BPM 이벤트가 없습니다.");

        var chunks = new List<byte[]>();
        var insertIndex = document.FirstMainLineIndex >= 0 ? document.FirstMainLineIndex : document.Lines.Count;
        var inserted = false;
        for (var index = 0; index < document.Lines.Count; index++)
        {
            if (index == insertIndex)
            {
                foreach (var text in insertion)
                {
                    chunks.Add(Encoding.ASCII.GetBytes(text));
                    chunks.Add(document.NewlineBytes);
                }
                inserted = true;
            }
            chunks.Add(document.Lines[index].Content);
            chunks.Add(document.Lines[index].Newline);
        }

        if (!inserted)
        {
            var last = document.Lines.LastOrDefault();
            if (last is not null && last.Newline.Length == 0 && last.Content.Length > 0)
                chunks.Add(document.NewlineBytes);

            for (var index = 0; index < insertion.Count; index++)
            {
                chunks.Add(Encoding.ASCII.GetBytes(insertion[index]));
                if (index < insertion.Count - 1)
                    chunks.Add(document.NewlineBytes);
            }
        }

        var length = chunks.Sum(chunk => chunk.Length);
        var output = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, output, offset, chunk.Length);
            offset += chunk.Length;
        }
        return output;
    }

    public static string OutputFileName(string inputName)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputName);
        return $"{baseName}_linear_bpm.bms";
    }

    public static string OutputPath(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, OutputFileName(inputPath));
}
