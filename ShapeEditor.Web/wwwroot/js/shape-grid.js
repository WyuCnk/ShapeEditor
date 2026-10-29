const crystalBandCache = new Map();

window.shapeGrid = {
    init: function (canvas, dotNetObject) {
        let resizeTimer;

        window.addEventListener("resize", function () {
            clearTimeout(resizeTimer);

            resizeTimer = setTimeout(function () {
                dotNetObject.invokeMethodAsync(
                    "OnCanvasResize");
            }, 100);
        });

        canvas._shapeGridDotNet = dotNetObject;
        canvas._shapeGridPointers = new Map();

        canvas.addEventListener("pointerdown", function (event) {
            if (event.button !== undefined &&
                event.button !== 0) {
                return;
            }

            canvas.setPointerCapture(event.pointerId);

            canvas._shapeGridPointers.set(
                event.pointerId,
                {
                    x: event.offsetX,
                    y: event.offsetY
                });

            const pointers =
                Array.from(canvas._shapeGridPointers.values());

            if (pointers.length >= 2) {
                const gesture =
                    getGestureInfo(pointers[0], pointers[1]);

                dotNetObject.invokeMethodAsync(
                    "OnCanvasTransformStart",
                    gesture.distance,
                    gesture.centerX,
                    gesture.centerY);
            }
            else {
                dotNetObject.invokeMethodAsync(
                    "OnCanvasPointerDown",
                    event.pointerId,
                    event.offsetX,
                    event.offsetY,
                    pointers.length);
            }
        });

        canvas.addEventListener("pointermove", function (event) {
            if (!canvas._shapeGridPointers.has(
                event.pointerId)) {
                return;
            }

            canvas._shapeGridPointers.set(
                event.pointerId,
                {
                    x: event.offsetX,
                    y: event.offsetY
                });

            const pointers =
                Array.from(canvas._shapeGridPointers.values());

            if (pointers.length >= 2) {
                const gesture =
                    getGestureInfo(pointers[0], pointers[1]);

                dotNetObject.invokeMethodAsync(
                    "OnCanvasTransform",
                    gesture.distance,
                    gesture.centerX,
                    gesture.centerY);
            }
            else {
                dotNetObject.invokeMethodAsync(
                    "OnCanvasPointerMove",
                    event.pointerId,
                    event.offsetX,
                    event.offsetY,
                    pointers.length);
            }
        });

        canvas.addEventListener("pointerup", function (event) {
            canvas._shapeGridPointers.delete(
                event.pointerId);

            const pointers =
                Array.from(canvas._shapeGridPointers.values());

            dotNetObject.invokeMethodAsync(
                "OnCanvasPointerUp",
                event.pointerId,
                event.offsetX,
                event.offsetY,
                pointers.length);
        });

        canvas.addEventListener("pointercancel", function (event) {
            canvas._shapeGridPointers.delete(
                event.pointerId);

            dotNetObject.invokeMethodAsync(
                "OnCanvasPointerUp",
                event.pointerId,
                event.offsetX,
                event.offsetY,
                canvas._shapeGridPointers.size);
        });
    },

    render: function (canvas, state) {
        const columns = state.columns;
        const rows = state.rows;
        const cells = state.cells;

        const viewWidth = canvas.clientWidth;
        const viewHeight = canvas.clientHeight;

        if (viewWidth <= 0 || viewHeight <= 0 || columns <= 0 || rows <= 0) {
            return;
        }

        /*
         * 主题常量来自 ShapeEditor.Core（见 ShapeThemeData），
         * 这里只保留一份兜底默认值，正常情况下不会用到。
         */
        const theme = resolveTheme(state);

        const baseCellWidth = theme.cellWidth;
        const baseCellHeight = theme.cellHeight;
        const margin = theme.fitMargin;

        const fitScale = Math.min(
            (viewWidth - margin * 2) / (columns * baseCellWidth),
            (viewHeight - margin * 2) / (rows * baseCellHeight),
            theme.maxFitScale
        );

        const cellScale = Math.max(0.05, fitScale);
        const cellWidth = baseCellWidth * cellScale;
        const cellHeight = baseCellHeight * cellScale;

        const gridWidth = columns * cellWidth;
        const gridHeight = rows * cellHeight;

        // 这是矩阵在视口内的基础居中位置，不包含用户平移。
        const originX = (viewWidth - gridWidth) / 2;
        const originY = (viewHeight - gridHeight) / 2;

        const dpr = window.devicePixelRatio || 1;
        canvas.width = Math.round(viewWidth * dpr);
        canvas.height = Math.round(viewHeight * dpr);

        const ctx = canvas.getContext("2d");
        if (!ctx) {
            return;
        }

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(0, 0, viewWidth, viewHeight);

        const panX = state.panX || 0;
        const panY = state.panY || 0;
        const zoom = state.zoom || 1;

        // C# 命中检测需要与绘制完全相同的单元格尺寸和基础原点。
        canvas._shapeGridDotNet?.invokeMethodAsync(
            "OnCanvasMetrics",
            cellWidth,
            cellHeight,
            originX,
            originY
        );

        ctx.save();
        ctx.translate(originX + panX, originY + panY);
        ctx.scale(zoom, zoom);

        for (let row = 0; row < rows; row++) {
            for (let column = 0; column < columns; column++) {
                const cell = cells[row * columns + column];

                drawCell(
                    ctx,
                    cell,
                    column * cellWidth,
                    row * cellHeight,
                    cellWidth,
                    cellHeight,
                    theme
                );
            }
        }

        drawGrid(ctx, rows, columns, cellWidth, cellHeight, theme);

        if (state.selectedLayer !== null && state.selectedPart !== null) {
            drawSelectedCell(
                ctx,
                state,
                state.selectedLayer,
                state.selectedPart,
                cellWidth,
                cellHeight,
                theme
            );
        }

        ctx.restore();
    },

    /*
     * 画布刚插入 DOM 时可能还没有布局尺寸，
     * 等到尺寸出现后再绘制，避免首帧空白。
     */
    renderWhenSized: function (canvas, state) {
        let attempts = 0;

        function attempt() {
            if (!canvas.isConnected) {
                return;
            }

            if (canvas.clientWidth > 0 &&
                canvas.clientHeight > 0) {
                window.shapeGrid.render(canvas, state);
                return;
            }

            attempts++;

            if (attempts < 20) {
                window.requestAnimationFrame(attempt);
            }
        }

        attempt();
    },

    downloadPng: function (canvas, fileName) {
        canvas.toBlob(function (blob) {
            if (!blob) {
                return;
            }

            const url =
                URL.createObjectURL(blob);

            const link =
                document.createElement("a");

            link.href = url;
            link.download = fileName;
            link.style.display = "none";

            document.body.appendChild(link);
            link.click();
            link.remove();

            URL.revokeObjectURL(url);
        }, "image/png");
    },

    downloadFullPng: async function (state, fileName) {
        const canvas =
            createFullImageCanvas(state);

        const blob =
            await canvasToBlob(canvas);

        const url =
            URL.createObjectURL(blob);

        const link =
            document.createElement("a");

        link.href = url;
        link.download = fileName;
        link.style.display = "none";

        document.body.appendChild(link);
        link.click();
        link.remove();

        URL.revokeObjectURL(url);
    },

    shareFullPng: async function (state, fileName, title) {
        const canvas =
            createFullImageCanvas(state);

        const blob =
            await canvasToBlob(canvas);

        const file =
            new File(
                [blob],
                fileName,
                {
                    type: "image/png"
                });

        if (navigator.share &&
            navigator.canShare &&
            navigator.canShare({
                files: [file]
            })) {
            try {
                await navigator.share({
                    title: title,
                    text: title,
                    files: [file]
                });
            }
            catch (error) {
                if (error.name !== "AbortError") {
                    throw error;
                }
            }

            return;
        }

        const url =
            URL.createObjectURL(blob);

        const link =
            document.createElement("a");

        link.href = url;
        link.download = fileName;
        link.style.display = "none";

        document.body.appendChild(link);
        link.click();
        link.remove();

        URL.revokeObjectURL(url);
    },
};

function createFullImageCanvas(state) {
    const theme = resolveTheme(state);

    const cellWidth = theme.exportCellWidth;
    const cellHeight = theme.exportCellHeight;

    const width =
        state.columns * cellWidth;

    const height =
        state.rows * cellHeight;

    const maxImageSide = theme.maxExportSide;

    if (width > maxImageSide ||
        height > maxImageSide) {
        throw new Error(
            `图片尺寸过大：${width} × ${height}`);
    }

    const dpr =
        window.devicePixelRatio || 1;

    const canvas =
        document.createElement("canvas");

    canvas.width =
        Math.round(width * dpr);

    canvas.height =
        Math.round(height * dpr);

    canvas.style.width =
        `${width}px`;

    canvas.style.height =
        `${height}px`;

    const ctx =
        canvas.getContext("2d");

    ctx.setTransform(
        dpr,
        0,
        0,
        dpr,
        0,
        0);

    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(
        0,
        0,
        width,
        height);

    for (let row = 0;
        row < state.rows;
        row++) {
        for (let column = 0;
            column < state.columns;
            column++) {
            const index =
                row * state.columns + column;

            const cell =
                state.cells[index];

            drawCell(
                ctx,
                cell,
                column * cellWidth,
                row * cellHeight,
                cellWidth,
                cellHeight,
                theme);
        }
    }

    drawGrid(
        ctx,
        state.rows,
        state.columns,
        cellWidth,
        cellHeight,
        theme);

    return canvas;
}

function drawCell(ctx, cell, x, y, width, height, theme) {
    if (cell.type === "empty") {
        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(x, y, width, height);
        return;
    }

    if (cell.type === "pin") {
        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(x, y, width, height);

        ctx.fillStyle = "#000000";

        const barWidth =
            width * theme.pinBarRatio;

        ctx.fillRect(
            x + (width - barWidth) / 2,
            y,
            barWidth,
            height);

        return;
    }

    const color =
        getColor(cell.colorCode, theme);

    if (cell.type === "crystal") {
        drawCrystalBands(
            ctx,
            cell.colorCode,
            color,
            x,
            y,
            width,
            height,
            theme);

        drawLightBorder(
            ctx,
            x,
            y,
            width,
            height,
            theme);
    }
    else {
        ctx.fillStyle = color;
        ctx.fillRect(x, y, width, height);

        drawLightBorder(
            ctx,
            x,
            y,
            width,
            height,
            theme);
    }

    const fontSize =
        Math.max(
            14,
            Math.min(
                72,
                Math.min(width, height) * 0.46));

    ctx.font = `bold ${fontSize}px "Microsoft YaHei", "Noto Sans CJK SC", "Noto Sans SC", sans-serif`;

    ctx.fillStyle =
        cell.colorCode === "k"
            ? "#FFFFFF"
            : "#000000";

    ctx.textAlign = "center";
    ctx.textBaseline = "middle";

    ctx.fillText(
        cell.type === "crystal"
            ? "晶"
            : "图",
        x + width / 2,
        y + height / 2);
}

function drawCrystalBands(
    ctx,
    colorCode,
    baseColor,
    x,
    y,
    width,
    height,
    theme) {
    const colors =
        getCrystalBands(theme, colorCode, baseColor);

    const bandWidth =
        width / colors.length;

    for (let i = 0; i < colors.length; i++) {
        ctx.fillStyle = colors[i];

        const bandX =
            x + i * bandWidth;

        const actualWidth =
            i === colors.length - 1
                ? x + width - bandX
                : bandWidth;

        ctx.fillRect(
            bandX,
            y,
            actualWidth,
            height);
    }
}

/*
 * 晶体条带优先取 Core 计算好的颜色；
 * 只有主题数据缺失时才在浏览器里现算。
 */
function getCrystalBands(theme, colorCode, baseColor) {
    if (theme &&
        theme.crystalBands &&
        theme.crystalBands[colorCode]) {
        return theme.crystalBands[colorCode];
    }

    return createCrystalBands(baseColor);
}

function createCrystalBands(baseColor) {
    if (crystalBandCache.has(baseColor)) {
        return crystalBandCache.get(baseColor);
    }

    const rgb = hexToRgb(baseColor);

    const edge =
        adjustColor(rgb, 0.82);

    const shoulder =
        mixWithWhite(rgb, 0.12);

    const highlight =
        mixWithWhite(rgb, 0.34);

    const bands = [
        rgbToHex(edge),
        rgbToHex(rgb),
        rgbToHex(shoulder),
        rgbToHex(highlight),
        rgbToHex(shoulder),
        rgbToHex(rgb),
        rgbToHex(edge)
    ];

    crystalBandCache.set(
        baseColor,
        bands);

    return bands;
}

function drawLightBorder(
    ctx,
    x,
    y,
    width,
    height,
    theme) {
    ctx.strokeStyle =
        `rgba(255,255,255,${theme.cellBorderOpacity})`;

    ctx.lineWidth =
        Math.max(
            1,
            Math.min(
                4,
                Math.min(width, height) * 0.025));

    const inset =
        Math.max(
            2,
            Math.min(width, height) * 0.035);

    ctx.strokeRect(
        x + inset,
        y + inset,
        width - inset * 2,
        height - inset * 2);
}

function drawGrid(
    ctx,
    rows,
    columns,
    cellWidth,
    cellHeight,
    theme) {
    const width =
        columns * cellWidth;

    const height =
        rows * cellHeight;

    ctx.save();

    ctx.strokeStyle = theme.gridLineColor;
    ctx.lineWidth = 1;

    /*
     * 将外边框向内缩半个像素，
     * 避免线条落在 Canvas 边界上被裁剪。
     */
    const left = 0.5;
    const top = 0.5;
    const right = Math.max(
        left,
        width - 0.5);

    const bottom = Math.max(
        top,
        height - 0.5);

    for (let column = 0;
        column <= columns;
        column++) {
        const x =
            column === columns
                ? right
                : column * cellWidth;

        ctx.beginPath();
        ctx.moveTo(x, top);
        ctx.lineTo(x, bottom);
        ctx.stroke();
    }

    for (let row = 0;
        row <= rows;
        row++) {
        const y =
            row === rows
                ? bottom
                : row * cellHeight;

        ctx.beginPath();
        ctx.moveTo(left, y);
        ctx.lineTo(right, y);
        ctx.stroke();
    }

    ctx.restore();
}

const FALLBACK_THEME = {
    colors: {
        r: "#FF3864",
        g: "#83FF38",
        b: "#65B7FF",
        c: "#35E6C1",
        m: "#D85CFF",
        y: "#FFB83E",
        w: "#D8D8D8",
        k: "#35353C",
        u: "#A9BAC8"
    },
    cellWidth: 100,
    cellHeight: 76,
    fitMargin: 12,
    maxFitScale: 1.45,
    gridLineColor: "#9E9E9E",
    selectionColor: "#1976D2",
    cellBorderOpacity: 0.72,
    pinBarRatio: 1 / 3,
    exportCellWidth: 128,
    exportCellHeight: 96,
    maxExportSide: 16000
};

/*
 * 主题由 C# 端（ShapeEditor.Core）随每次渲染一起传下来，
 * 正常情况一定存在；FALLBACK_THEME 只用于极端情况下兜底。
 */
function resolveTheme(state) {
    if (state && state.theme) {
        return state.theme;
    }

    return FALLBACK_THEME;
}

function getColor(code, theme) {
    if (theme && theme.colors && theme.colors[code]) {
        return theme.colors[code];
    }

    switch (code) {
        case "r": return "#FF3864";
        case "g": return "#83FF38";
        case "b": return "#65B7FF";
        case "c": return "#35E6C1";
        case "m": return "#D85CFF";
        case "y": return "#FFB83E";
        case "w": return "#D8D8D8";
        case "k": return "#35353C";
        case "u": return "#A9BAC8";
        default: return "#FFFFFF";
    }
}

function hexToRgb(hex) {
    return {
        r: parseInt(hex.substring(1, 3), 16),
        g: parseInt(hex.substring(3, 5), 16),
        b: parseInt(hex.substring(5, 7), 16)
    };
}

function adjustColor(rgb, factor) {
    return {
        r: Math.round(rgb.r * factor),
        g: Math.round(rgb.g * factor),
        b: Math.round(rgb.b * factor)
    };
}

function mixWithWhite(rgb, amount) {
    return {
        r: Math.round(rgb.r + (255 - rgb.r) * amount),
        g: Math.round(rgb.g + (255 - rgb.g) * amount),
        b: Math.round(rgb.b + (255 - rgb.b) * amount)
    };
}

function rgbToHex(rgb) {
    return "#" +
        [rgb.r, rgb.g, rgb.b]
            .map(value =>
                value.toString(16).padStart(2, "0"))
            .join("");
}

function drawSelectedCell(
    ctx,
    state,
    layer,
    part,
    cellWidth,
    cellHeight,
    theme) {
    const visualRow =
        state.rows - 1 - layer;

    const x =
        part * cellWidth;

    const y =
        visualRow * cellHeight;

    const inset =
        Math.max(
            2,
            Math.min(
                cellWidth,
                cellHeight) * 0.035);

    ctx.save();

    ctx.strokeStyle = theme.selectionColor;
    ctx.lineWidth = Math.max(
        2,
        Math.min(
            5,
            Math.min(
                cellWidth,
                cellHeight) * 0.035));

    ctx.strokeRect(
        x + inset,
        y + inset,
        cellWidth - inset * 2,
        cellHeight - inset * 2);

    ctx.restore();
}

function getGestureInfo(first, second) {
    const dx = second.x - first.x;
    const dy = second.y - first.y;

    return {
        distance: Math.sqrt(dx * dx + dy * dy),
        centerX: (first.x + second.x) / 2,
        centerY: (first.y + second.y) / 2
    };
}

function canvasToBlob(canvas) {
    return new Promise(function (resolve, reject) {
        canvas.toBlob(function (blob) {
            if (blob) {
                resolve(blob);
            }
            else {
                reject(new Error("无法生成 PNG 图片。"));
            }
        }, "image/png");
    });
}
/*
 * 合成导出：把若干竖式图按“输入 → 产物”的横向流程拼成一张图。
 *
 * sections: [{ label, states: [ShapeCanvasState], arrowBefore: bool }]
 * 同一 section 内多张图纵向排列（切割的东西两半用得到）。
 */
window.shapeGrid.downloadComposition = async function (sections, fileName) {
    const padding = 28;
    const labelHeight = 30;
    const gap = 14;
    const arrowWidth = 56;
    const maxSide = 16000;

    const rendered = [];

    for (let i = 0; i < sections.length; i++) {
        const section = sections[i];
        const images = [];

        for (let j = 0; j < section.states.length; j++) {
            images.push(createFullImageCanvas(section.states[j]));
        }

        rendered.push({
            label: section.label || "",
            images: images,
            arrowBefore: !!section.arrowBefore
        });
    }

    const widths = rendered.map(function (section) {
        let width = 120;

        for (let i = 0; i < section.images.length; i++) {
            width = Math.max(width, section.images[i].width);
        }

        return width;
    });

    const heights = rendered.map(function (section) {
        let height = 0;

        for (let i = 0; i < section.images.length; i++) {
            height += section.images[i].height;

            if (i < section.images.length - 1) {
                height += gap;
            }
        }

        return height;
    });

    let totalWidth = padding * 2;

    for (let i = 0; i < rendered.length; i++) {
        totalWidth += widths[i];

        if (i > 0 && rendered[i].arrowBefore) {
            totalWidth += arrowWidth;
        }
    }

    const maxHeight = Math.max.apply(null, heights);
    const totalHeight = padding * 2 + labelHeight + maxHeight;

    if (totalWidth > maxSide || totalHeight > maxSide) {
        throw new Error(`图片尺寸过大：${totalWidth} × ${totalHeight}`);
    }

    const canvas = document.createElement("canvas");
    canvas.width = totalWidth;
    canvas.height = totalHeight;

    const ctx = canvas.getContext("2d");

    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, totalWidth, totalHeight);

    ctx.font = '600 16px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    ctx.fillStyle = "#212121";
    ctx.textAlign = "center";
    ctx.textBaseline = "top";

    let x = padding;

    for (let i = 0; i < rendered.length; i++) {
        const section = rendered[i];

        if (i > 0 && section.arrowBefore) {
            ctx.fillText(
                "→",
                x + arrowWidth / 2,
                padding + labelHeight + maxHeight / 2 - 12);

            x += arrowWidth;
        }

        ctx.fillText(section.label, x + widths[i] / 2, padding);

        let y = padding + labelHeight;

        for (let j = 0; j < section.images.length; j++) {
            const image = section.images[j];

            ctx.drawImage(image, x + (widths[i] - image.width) / 2, y);

            y += image.height + gap;
        }

        x += widths[i];
    }

    const blob = await canvasToBlob(canvas);
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    link.click();
    link.remove();

    URL.revokeObjectURL(url);
};
/*
 * 合成导出（布局版）：左侧一列输入，箭头，右侧一列产物。
 * 堆叠：左侧上=上方图形、下=下方图形；右侧=产物（垂直居中）。
 * 切割：左侧=原图形；右侧=西半(上)、东半(下)。
 */
window.shapeGrid.downloadComposition = async function (sections, fileName, operation) {
    const padding = 28;
    const titleHeight = 44;
    const labelHeight = 26;
    const gap = 16;
    const arrowWidth = 64;
    const maxSide = 16000;

    const titles = {
        stack: "堆叠",
        cut: "切割",
        pin: "顶针",
        collapse: "下落"
    };

    if (!sections || sections.length < 2) {
        return;
    }

    function toImage(section) {
        return createFullImageCanvas(section.states[0]);
    }

    function build(section, label) {
        return { label: label, image: toImage(section) };
    }

    let left = [];
    let right = [];

    if (operation === "stack" && sections.length >= 3) {
        // 左侧从上到下：上方、下方
        left = [build(sections[1], "上方图形"), build(sections[0], "下方图形")];
        right = [build(sections[2], "堆叠产物")];
    }
    else if (operation === "cut" && sections.length === 2) {
        left = [build(sections[0], "原图形")];

        const states = sections[1].states;
        right = [
            { label: "西半", image: createFullImageCanvas(states[0]) },
            { label: "东半", image: createFullImageCanvas(states[1]) }
        ];
    }
    else {
        left = [build(sections[0], "原图形")];
        right = [build(sections[1], sections[1].label || "产物")];
    }

    function columnWidth(items) {
        let width = 120;

        for (let i = 0; i < items.length; i++) {
            width = Math.max(width, items[i].image.width);
        }

        return width;
    }

    function columnHeight(items) {
        let height = 0;

        for (let i = 0; i < items.length; i++) {
            height += items[i].image.height + labelHeight;

            if (i < items.length - 1) {
                height += gap;
            }
        }

        return height;
    }

    const leftWidth = columnWidth(left);
    const rightWidth = columnWidth(right);
    const leftHeight = columnHeight(left);
    const rightHeight = columnHeight(right);

    const contentHeight = Math.max(leftHeight, rightHeight);
    const totalWidth = padding * 2 + leftWidth + arrowWidth + rightWidth;
    const totalHeight = padding * 2 + titleHeight + contentHeight;

    if (totalWidth > maxSide || totalHeight > maxSide) {
        throw new Error("图片尺寸过大：" + totalWidth + " × " + totalHeight);
    }

    const canvas = document.createElement("canvas");
    canvas.width = totalWidth;
    canvas.height = totalHeight;

    const ctx = canvas.getContext("2d");

    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, totalWidth, totalHeight);

    ctx.fillStyle = "#212121";
    ctx.textAlign = "center";
    ctx.textBaseline = "top";

    ctx.font = '700 22px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    ctx.fillText(titles[operation] || "", totalWidth / 2, padding);

    function drawColumn(items, x, columnH) {
        let y = padding + titleHeight + (contentHeight - columnH) / 2;

        for (let i = 0; i < items.length; i++) {
            const item = items[i];

            ctx.font = '400 14px "Microsoft YaHei", "Noto Sans SC", sans-serif';
            ctx.fillStyle = "#6B6B76";
            ctx.fillText(item.label, x + leftWidthOr(item), y);

            ctx.drawImage(
                item.image,
                x + (columnWidth(items) - item.image.width) / 2,
                y + labelHeight);

            y += item.image.height + labelHeight + gap;
        }
    }

    function leftWidthOr(item) {
        return 0;
    }

    // 左列
    let yLeft = padding + titleHeight + (contentHeight - leftHeight) / 2;

    for (let i = 0; i < left.length; i++) {
        const item = left[i];

        ctx.font = '400 14px "Microsoft YaHei", "Noto Sans SC", sans-serif';
        ctx.fillStyle = "#6B6B76";
        ctx.fillText(item.label, padding + leftWidth / 2, yLeft);

        ctx.drawImage(
            item.image,
            padding + (leftWidth - item.image.width) / 2,
            yLeft + labelHeight);

        yLeft += item.image.height + labelHeight + gap;
    }

    // 箭头
    const arrowX = padding + leftWidth + arrowWidth / 2;
    const arrowY = padding + titleHeight + contentHeight / 2;

    ctx.font = '700 34px "Microsoft YaHei", sans-serif';
    ctx.fillStyle = "#512BD4";
    ctx.fillText("→", arrowX, arrowY - 22);

    // 右列
    const rightX = padding + leftWidth + arrowWidth;
    let yRight = padding + titleHeight + (contentHeight - rightHeight) / 2;

    for (let i = 0; i < right.length; i++) {
        const item = right[i];

        ctx.font = '400 14px "Microsoft YaHei", "Noto Sans SC", sans-serif';
        ctx.fillStyle = "#6B6B76";
        ctx.fillText(item.label, rightX + rightWidth / 2, yRight);

        ctx.drawImage(
            item.image,
            rightX + (rightWidth - item.image.width) / 2,
            yRight + labelHeight);

        yRight += item.image.height + labelHeight + gap;
    }

    const blob = await canvasToBlob(canvas);
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    link.click();
    link.remove();

    URL.revokeObjectURL(url);
};
/*
 * 路径图导出：按节点 row/col 重绘整张制作路径。
 * 主数据流用实线 + 操作标签；引用关系用虚线 + #编号（远距离只标编号）。
 */
window.shapeGrid.downloadGraph = async function (payload, fileName) {
    const CELL_W = 250;
    const CELL_H = 360;
    const NODE_W = 210;
    const NODE_H = 280;
    const PAD = 24;
    const maxSide = 16000;

    const nodes = payload.nodes || [];
    const edges = payload.edges || [];

    if (nodes.length === 0) {
        return;
    }

    const byId = {};

    let maxCol = 0;
    let maxRow = 0;

    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];

        byId[node.id] = node;
        maxCol = Math.max(maxCol, node.col);
        maxRow = Math.max(maxRow, node.row);
    }

    const width = PAD * 2 + (maxCol + 1) * CELL_W + (NODE_W - CELL_W);
    const height = PAD * 2 + (maxRow + 1) * CELL_H + (NODE_H - CELL_H);

    if (width > maxSide || height > maxSide) {
        throw new Error("图片尺寸过大：" + width + " × " + height);
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;

    const ctx = canvas.getContext("2d");

    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, width, height);

    function nodeLeft(node) { return PAD + node.col * CELL_W; }
    function nodeTop(node) { return PAD + node.row * CELL_H; }

    // ---- 连线 ----
    const outIndex = {};
    const outCount = {};
    const inIndex = {};
    const inCount = {};

    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];

        outIndex[i] = (outCount[edge.from] || 0);
        outCount[edge.from] = (outCount[edge.from] || 0) + 1;
        inIndex[i] = (inCount[edge.to] || 0);
        inCount[edge.to] = (inCount[edge.to] || 0) + 1;
    }

    function spread(index, count, step) {
        if (count <= 1) {
            return 0;
        }

        return Math.round((index - (count - 1) / 2) * step);
    }

    function arrowHead(x, y, dashed) {
        ctx.beginPath();
        ctx.moveTo(x - 5, y - 9);
        ctx.lineTo(x + 5, y - 9);
        ctx.lineTo(x, y);
        ctx.closePath();
        ctx.fillStyle = dashed ? "#8B7BBF" : "#512BD4";
        ctx.fill();
    }

    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) {
            continue;
        }

        if (edge.kind === "ref") {
            const far = Math.abs(from.row - to.row) > 2 || Math.abs(from.col - to.col) > 2;

            if (far) {
                // 远距离引用只标编号，不画长线
                ctx.font = '600 12px "Microsoft YaHei", sans-serif';
                ctx.fillStyle = "#8B7BBF";
                ctx.textAlign = "right";
                ctx.textBaseline = "top";
                ctx.fillText(edge.label, nodeLeft(to) + NODE_W - 6, nodeTop(to) + 6);

                continue;
            }

            const x1 = nodeLeft(from) + NODE_W / 2;
            const y1 = nodeTop(from) + NODE_H;
            const x2 = nodeLeft(to) + NODE_W / 2;

            ctx.save();
            ctx.setLineDash([6, 5]);
            ctx.strokeStyle = "#8B7BBF";
            ctx.lineWidth = 1.6;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x1, y1 + 24);
            ctx.lineTo(x2, y1 + 24);
            ctx.lineTo(x2, nodeTop(to) - 6);
            ctx.stroke();
            ctx.restore();

            arrowHead(x2, nodeTop(to), true);

            continue;
        }

        const x1 = nodeLeft(from) + NODE_W / 2 + spread(outIndex[i], outCount[edge.from], 30);
        const x2 = nodeLeft(to) + NODE_W / 2 + spread(inIndex[i], inCount[edge.to], 30);

        const y1 = nodeTop(from) + NODE_H;
        const y2 = nodeTop(to);

        let midY = y1 + 24 + outIndex[i] * 20;

        if (midY > y2 - 22) {
            midY = y2 - 22;
        }

        if (midY < y1 + 22) {
            midY = y1 + 22;
        }

        ctx.strokeStyle = "#512BD4";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x1, midY);
        ctx.lineTo(x2, midY);
        ctx.lineTo(x2, y2 - 4);
        ctx.stroke();

        arrowHead(x2, y2, false);

        // 标签加白底，避免压线
        ctx.font = '400 12px "Microsoft YaHei", sans-serif';
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";

        const labelX = Math.abs(x1 - x2) < 8 ? x1 + 34 : (x1 + x2) / 2;
        const textWidth = ctx.measureText(edge.label).width;

        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(labelX - textWidth / 2 - 4, midY - 16, textWidth + 8, 16);

        ctx.fillStyle = "#4527A0";
        ctx.fillText(edge.label, labelX, midY - 8);
    }

    // ---- 节点 ----
    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];

        const x = nodeLeft(node);
        const y = nodeTop(node);

        ctx.fillStyle = node.isResult ? "#FAF8FF" : "#FFFFFF";
        ctx.strokeStyle = node.isResult ? "#512BD4" : (node.isRef ? "#B9A9EE" : "#D0D0D0");
        ctx.lineWidth = node.isResult ? 2 : 1.2;

        ctx.beginPath();
        ctx.roundRect(x, y, NODE_W, NODE_H, 12);
        ctx.fill();
        ctx.stroke();

        // 标题
        ctx.font = '600 12px "Microsoft YaHei", sans-serif';
        ctx.textAlign = "left";
        ctx.textBaseline = "top";
        ctx.fillStyle = "#6B6B76";
        ctx.fillText("#" + node.seq + " · " + node.label, x + 10, y + 10);

        if (node.refText) {
            ctx.font = '600 12px "Microsoft YaHei", sans-serif';
            ctx.fillStyle = "#8B7BBF";
            ctx.fillText(node.refText, x + 10, y + 26);
        }

        // 缩略图
        if (node.state) {
            const image = createFullImageCanvas(node.state);

            const boxX = x + 10;
            const boxY = y + 40;
            const boxW = NODE_W - 20;
            const boxH = 120;

            const scale = Math.min(boxW / image.width, boxH / image.height);
            const drawW = image.width * scale;
            const drawH = image.height * scale;

            ctx.drawImage(
                image,
                boxX + (boxW - drawW) / 2,
                boxY + (boxH - drawH) / 2,
                drawW,
                drawH);
        }
    }

    const blob = await canvasToBlob(canvas);
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    link.click();
    link.remove();

    URL.revokeObjectURL(url);
};