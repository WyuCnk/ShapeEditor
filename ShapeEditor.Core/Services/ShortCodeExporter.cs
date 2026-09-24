using System.Text;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ShortCodeExporter
{
    public string Export(
        ShapeDocument document,
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
                builder.Append(
                    document[layer, part]
                        .ToShortCode());
            }
        }

        return builder.ToString();
    }
}