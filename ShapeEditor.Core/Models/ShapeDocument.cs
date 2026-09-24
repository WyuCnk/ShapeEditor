namespace ShapeEditor.Core.Models;

public sealed class ShapeDocument
{
    private readonly ShapeCell[,] _cells;

    public int LayerCount { get; private set; }

    public int PartCount { get; private set; }

    public ShapeDocument(
        int layerCount,
        int partCount)
    {
        if (layerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(layerCount));
        }

        if (partCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partCount));
        }

        LayerCount = layerCount;
        PartCount = partCount;

        _cells = new ShapeCell[
            layerCount,
            partCount];

        Clear();
    }

    /// <summary>
    /// layer 0 是最低层。
    /// part 0 是该层短代码中的第一个象限。
    /// </summary>
    public ShapeCell this[
        int layer,
        int part]
    {
        get
        {
            CheckIndex(layer, part);
            return _cells[layer, part];
        }
        set
        {
            CheckIndex(layer, part);
            _cells[layer, part] =
                value ?? throw new ArgumentNullException(
                    nameof(value));
        }
    }

    public void Clear()
    {
        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < PartCount;
                 part++)
            {
                _cells[layer, part] =
                    ShapeCell.Empty();
            }
        }
    }

    /// <summary>
    /// 调整尺寸并尽可能保留原有内容。
    /// 多出的层和象限填充为空。
    /// 被截断的内容不会保留。
    /// </summary>
    public ShapeDocument Resize(
        int newLayerCount,
        int newPartCount)
    {
        var result = new ShapeDocument(
            newLayerCount,
            newPartCount);

        int copyLayers = Math.Min(
            LayerCount,
            newLayerCount);

        int copyParts = Math.Min(
            PartCount,
            newPartCount);

        for (int layer = 0;
             layer < copyLayers;
             layer++)
        {
            for (int part = 0;
                 part < copyParts;
                 part++)
            {
                result[layer, part] =
                    this[layer, part].Clone();
            }
        }

        return result;
    }

    #region 复制

    /// <summary>
    /// 深度复制当前图形文档。
    /// </summary>
    public ShapeDocument Clone()
    {
        var result = new ShapeDocument(
            LayerCount,
            PartCount);

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < PartCount;
                 part++)
            {
                result[layer, part] =
                    this[layer, part].Clone();
            }
        }

        return result;
    }

    #endregion

    public bool IsLayerEmpty(int layer)
    {
        if (layer < 0 || layer >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(layer));
        }

        for (int part = 0;
             part < PartCount;
             part++)
        {
            if (this[layer, part].Type != CellType.Empty)
            {
                return false;
            }
        }

        return true;
    }

    private void CheckIndex(
        int layer,
        int part)
    {
        if (layer < 0 || layer >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(layer));
        }

        if (part < 0 || part >= PartCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(part));
        }
    }

    #region 数据检查

    /// <summary>
    /// 判断调整到新尺寸时，是否会删除已有的非空单元格。
    /// </summary>
    public bool HasContentOutside(
        int newLayerCount,
        int newPartCount)
    {
        if (newLayerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newLayerCount));
        }

        if (newPartCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newPartCount));
        }

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < PartCount;
                 part++)
            {
                bool outside =
                    layer >= newLayerCount ||
                    part >= newPartCount;

                if (outside &&
                    this[layer, part].Type != CellType.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region 整体移动

    public void MoveUpOneLayer()
    {
        for (int layer = LayerCount - 1;
             layer >= 1;
             layer--)
        {
            for (int part = 0;
                 part < PartCount;
                 part++)
            {
                this[layer, part] =
                    this[layer - 1, part];
            }
        }

        ClearLayer(0);
    }

    public void MoveDownOneLayer()
    {
        for (int layer = 0;
             layer < LayerCount - 1;
             layer++)
        {
            for (int part = 0;
                 part < PartCount;
                 part++)
            {
                this[layer, part] =
                    this[layer + 1, part];
            }
        }

        ClearLayer(LayerCount - 1);
    }

    public void MoveLeftCircular()
    {
        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            ShapeCell first =
                this[layer, 0].Clone();

            for (int part = 0;
                 part < PartCount - 1;
                 part++)
            {
                this[layer, part] =
                    this[layer, part + 1];
            }

            this[layer, PartCount - 1] =
                first;
        }
    }

    public void MoveRightCircular()
    {
        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            ShapeCell last =
                this[
                    layer,
                    PartCount - 1].Clone();

            for (int part = PartCount - 1;
                 part >= 1;
                 part--)
            {
                this[layer, part] =
                    this[layer, part - 1];
            }

            this[layer, 0] =
                last;
        }
    }

    private void ClearLayer(
    int layer)
    {
        for (int part = 0;
             part < PartCount;
             part++)
        {
            this[layer, part] =
                ShapeCell.Empty();
        }
    }

    #endregion

}