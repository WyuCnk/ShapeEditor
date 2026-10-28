using System.Text;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ShortCodeExporter
{
    public string Export(
        ShapeDocument document,
        bool omitTopEmptyLayers = true)
    {
        return Export(
            document,
            ShortCodeFormat.Full,
            omitTopEmptyLayers);
    }

    public string Export(
        ShapeDocument document,
        ShortCodeFormat format,
        bool omitTopEmptyLayers = true)
    {
        int highestLayer =
            document.LayerCount - 1;

        if (omitTopEmptyLayers)
        {
            while (highestLayer > 0 &&
                   document.IsLayerEmpty(highestLayer))
            {
                highestLayer--;
            }
        }

        var builder = new StringBuilder();

        for (int layer = 0;
             layer <= highestLayer;
             layer++)
        {
            if (layer > 0)
            {
                builder.Append(':');
            }

            for (int part = 0;
                 part < document.PartCount;
                 part++)
            {
                ShapeCell cell =
                    document[layer, part];

                builder.Append(
                    format == ShortCodeFormat.Super
                        ? cell.ToSuperShortCode()
                        : cell.ToShortCode());
            }
        }

        return builder.ToString();
    }
}