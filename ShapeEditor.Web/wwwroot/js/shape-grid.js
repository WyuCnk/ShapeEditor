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
            window.shapeGridSaveOrShare(link, fileName);
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
        window.shapeGridSaveOrShare(link, fileName);
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
        window.shapeGridSaveOrShare(link, fileName);
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
            ? "c"
            : "S",
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

    // 箭头单独一遍绘制，避免被后绘制的节点卡片遮住
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) { continue; }

        const a1 = edgeAnchor(from, to);
        const a2 = edgeAnchor(to, from);

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        const tipX = (edgeGeom[i] ? edgeGeom[i].x : a2.x) - vx * 3;
        const tipY = (edgeGeom[i] ? edgeGeom[i].y : a2.y) - vy * 3;

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(bx - vy * half, by + vx * half);
        ctx.lineTo(bx + vy * half, by - vx * half);
        ctx.closePath();
        ctx.fillStyle = "#512BD4";
        ctx.fill();
    }
    const blob = await canvasToBlob(canvas);
    if (await window.shapeGridTryShare(blob, fileName)) { return; }
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    window.shapeGridSaveOrShare(link, fileName);
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

    // 箭头单独一遍绘制，避免被后绘制的节点卡片遮住
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) { continue; }

        const a1 = edgeAnchor(from, to);
        const a2 = edgeAnchor(to, from);

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        const tipX = (edgeGeom[i] ? edgeGeom[i].x : a2.x) - vx * 3;
        const tipY = (edgeGeom[i] ? edgeGeom[i].y : a2.y) - vy * 3;

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(bx - vy * half, by + vx * half);
        ctx.lineTo(bx + vy * half, by - vx * half);
        ctx.closePath();
        ctx.fillStyle = "#512BD4";
        ctx.fill();
    }
    const blob = await canvasToBlob(canvas);
    if (await window.shapeGridTryShare(blob, fileName)) { return; }
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    window.shapeGridSaveOrShare(link, fileName);
    link.remove();

    URL.revokeObjectURL(url);
};
/*
 * 路径图导出：按节点 row/col 重绘整张制作路径。
 * 主数据流用实线 + 操作标签；引用关系用虚线 + #编号（远距离只标编号）。
 */
window.shapeGrid.downloadGraph = async function (payload, fileName) {
    const CELL_W = 250;
    const CELL_H = 400;
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

    const exportScale = 1;

    if (width * exportScale > 16000 || height * exportScale > 16000) {
        throw new Error("图片过大：" + Math.round(width * exportScale) + " × " +
            Math.round(height * exportScale) + " 像素，请减少节点数量后重试。");
    }

    canvas.width = Math.round(width * exportScale);
    canvas.height = Math.round(height * exportScale);

    ctx.scale(exportScale, exportScale);
    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, width, height);

function edgeAnchor(node, other) {
        const cx = nodeLeft(node) + (node._w || NODE_W) / 2;
        const cy = nodeTop(node) + (node._height || NODE_H) / 2;

        const ox = nodeLeft(other) + (other._w || NODE_W) / 2;
        const oy = nodeTop(other) + (other._height || NODE_H) / 2;

        let dx = ox - cx;
        let dy = oy - cy;
        const len = Math.sqrt(dx * dx + dy * dy);

        if (len < 1) {
            return { x: cx, y: cy + (node._height || NODE_H) / 2 };
        }

        dx /= len;
        dy /= len;

        const hw = (node._w || NODE_W) / 2;
        const hh = (node._height || NODE_H) / 2;

        const sx = Math.abs(dx) < 1e-6 ? Number.MAX_VALUE : hw / Math.abs(dx);
        const sy = Math.abs(dy) < 1e-6 ? Number.MAX_VALUE : hh / Math.abs(dy);

        const scale = Math.min(sx, sy);

        return { x: cx + dx * scale, y: cy + dy * scale };
    }

    function nodeLeft(node) { return PAD + node.col * CELL_W; }
    function nodeTop(node) { return PAD + node.row * CELL_H; }

    const edgeGeom = [];

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

    function arrowHeadAt(x1, y1, x2, y2, color) {
        const angle = Math.atan2(y2 - y1, x2 - x1);
        const size = 10;

        ctx.save();
        ctx.translate(x2, y2);
        ctx.rotate(angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(-size, -size * 0.55);
        ctx.lineTo(-size, size * 0.55);
        ctx.closePath();
        ctx.fillStyle = color;
        ctx.fill();
        ctx.restore();
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

        ctx.strokeStyle = "#512BD4";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();

        arrowHeadAt(x1, y1, x2, y2, "#512BD4");

        const t = Math.min(0.9, Math.max(0.1, edge.labelT === undefined ? 0.5 : edge.labelT));
        const dx = x2 - x1;
        const dy = y2 - y1;
        const length = Math.max(1, Math.sqrt(dx * dx + dy * dy));

        let nx = -dy / length;
        let ny = dx / length;

        if (ny > 0) {
            nx = -nx;
            ny = -ny;
        }

        if (Math.abs(dx) < 8) {
            nx = 1;
            ny = 0;
        }

        const offset = Math.abs(dx) < 8 ? 42 : 18;

        const midX = x1 + dx * t + nx * offset;
        const midY = y1 + dy * t + ny * offset;

        // 标签加白底，避免压线
        if (!edge.label) { continue; }
        ctx.font = '400 12px "Microsoft YaHei", sans-serif';
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";

        const labelX = midX;
        const textWidth = ctx.measureText(edge.label).width;

        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(labelX - textWidth / 2 - 6, midY - 9, textWidth + 12, 18);

        ctx.fillStyle = "#4527A0";
        ctx.fillText(edge.label, labelX, midY);
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

    // 箭头单独一遍绘制，避免被后绘制的节点卡片遮住
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) { continue; }

        const a1 = edgeAnchor(from, to);
        const a2 = edgeAnchor(to, from);

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        const tipX = (edgeGeom[i] ? edgeGeom[i].x : a2.x) - vx * 3;
        const tipY = (edgeGeom[i] ? edgeGeom[i].y : a2.y) - vy * 3;

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(bx - vy * half, by + vx * half);
        ctx.lineTo(bx + vy * half, by - vx * half);
        ctx.closePath();
        ctx.fillStyle = "#512BD4";
        ctx.fill();
    }
    const blob = await canvasToBlob(canvas);
    if (await window.shapeGridTryShare(blob, fileName)) { return; }
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    window.shapeGridSaveOrShare(link, fileName);
    link.remove();

    URL.revokeObjectURL(url);
};
/*
 * 路径图导出（自适应版）：节点高度按图形比例收紧，
 * 每行高度由该行最高节点决定，行间距固定。
 * 覆盖前面的 downloadGraph 实现。
 */
window.shapeGrid.downloadGraph = async function (payload, fileName) {
    const COL_W = 250;
    const NODE_W = 210;
    const PAD = 24;
    const GAP_Y = 90;
    const HEADER_H = 46;
    const MAX_IMAGE_W = NODE_W - 24;
    const MAX_IMAGE_H = 170;
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

        if (node.state) {
            const image = createFullImageCanvas(node.state);

            const scale = Math.min(
                MAX_IMAGE_W / image.width,
                MAX_IMAGE_H / image.height,
                1);

            node._image = image;
            node._imageW = Math.max(24, Math.round(image.width * scale));
            node._imageH = Math.max(24, Math.round(image.height * scale));
        }
        else {
            node._image = null;
            node._imageW = 0;
            node._imageH = 0;
        }

        node._height = node._image
            ? HEADER_H + node._imageH + 14
            : 120;
    }

    // 行高 = 该行最高节点
        // 与界面一致：节点高度按层数计算（最多 20 行）
    for (const item of nodes) {
        const layerCount = item.state && item.state.rows ? item.state.rows : 6;
        item._height = 50 + layerCount * 30;
        item._w = 16 + (item.state && item.state.columns ? item.state.columns : 12) * 30;
        if (item._image) {
            const availH = Math.max(20, item._height - 44 - 8);
            const fit = Math.min((NODE_W - 24) / item._image.width, availH / item._image.height, 1);

            item._imageW = Math.round(item._image.width * fit);
            item._imageH = Math.round(item._image.height * fit);
        }
    }
const rowHeights = [];

    for (let row = 0; row <= maxRow; row++) {
        let height = 120;

        for (let i = 0; i < nodes.length; i++) {
            if (nodes[i].row === row) {
                height = Math.max(height, nodes[i]._height);
            }
        }

        rowHeights[row] = height;
    }

    const rowY = [PAD];

    for (let row = 1; row <= maxRow; row++) {
        rowY[row] = rowY[row - 1] + rowHeights[row - 1] + GAP_Y;
    }

    const bounds = (() => {
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

        for (const n of nodes) {
            const nx = (n.x !== undefined ? n.x : n.col * COL_W);
            const ny = (n.y !== undefined ? n.y : rowY[n.row]);

            minX = Math.min(minX, nx);
            minY = Math.min(minY, ny);
            maxX = Math.max(maxX, nx + (n._w || NODE_W));
            maxY = Math.max(maxY, ny + (n._height || 220));
        }

        return { minX, minY, maxX, maxY };
    })();
const width = bounds.maxX - bounds.minX + PAD * 2;
    const height = bounds.maxY - bounds.minY + PAD * 2;

    if (width > maxSide || height > maxSide) {
        throw new Error("图片尺寸过大：" + width + " × " + height);
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;

    const ctx = canvas.getContext("2d");

    const exportScale = 1;

    if (width * exportScale > 16000 || height * exportScale > 16000) {
        throw new Error("图片过大：" + Math.round(width * exportScale) + " × " +
            Math.round(height * exportScale) + " 像素，请减少节点数量后重试。");
    }

    canvas.width = Math.round(width * exportScale);
    canvas.height = Math.round(height * exportScale);

    ctx.scale(exportScale, exportScale);
    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, width, height);

    function rawLeft(node) { return (node.x !== undefined ? node.x : node.col * COL_W); }
    function rawTop(node) { return (node.y !== undefined ? node.y : rowY[node.row]); }
    function nodeLeft(node) { return PAD + rawLeft(node) - bounds.minX; }
    function nodeTop(node) { return PAD + rawTop(node) - bounds.minY; }
    function edgeAnchor(node, other) {
        const cx = nodeLeft(node) + (node._w || NODE_W) / 2;
        const cy = nodeTop(node) + (node._height || 220) / 2;
        const ox = nodeLeft(other) + (other._w || NODE_W) / 2;
        const oy = nodeTop(other) + (other._height || 220) / 2;

        let dx = ox - cx;
        let dy = oy - cy;
        const len = Math.sqrt(dx * dx + dy * dy);

        if (len < 1) {
            return { x: cx, y: cy + (node._height || 220) / 2 };
        }

        dx /= len;
        dy /= len;

        const hw = (node._w || NODE_W) / 2;
        const hh = (node._height || 220) / 2;
        const sx = Math.abs(dx) < 1e-6 ? Number.MAX_VALUE : hw / Math.abs(dx);
        const sy = Math.abs(dy) < 1e-6 ? Number.MAX_VALUE : hh / Math.abs(dy);
        const scale = Math.min(sx, sy);

        return { x: cx + dx * scale, y: cy + dy * scale };
    }

    // 出入边序号，端点错开
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

    function arrowHeadAt(x1, y1, x2, y2) {
        const angle = Math.atan2(y2 - y1, x2 - x1);
        const size = 10;

        ctx.save();
        ctx.translate(x2, y2);
        ctx.rotate(angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(-size, -size * 0.55);
        ctx.lineTo(-size, size * 0.55);
        ctx.closePath();
        ctx.fillStyle = "#512BD4";
        ctx.fill();
        ctx.restore();
    }

    const edgeGeom = [];

    // ---- 连线 ----
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) {
            continue;
        }

        // 与界面一致：锚点取连心线与矩形边界交点，多条边沿法线错开
        const a1 = edgeAnchor(from, to);
        const a2 = edgeAnchor(to, from);

        const vx = a2.x - a1.x;
        const vy = a2.y - a1.y;
        const vlen = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        const px = -vy / vlen;
        const py = vx / vlen;

        const o1 = 0;
        const o2 = 0;

        const x1 = a1.x + px * o1;
        const y1 = a1.y + py * o1;
        const x2 = a2.x + px * o2;
        const y2 = a2.y + py * o2;

        edgeGeom[i] = { x: x2, y: y2, nx: px, ny: py };

        ctx.strokeStyle = "#512BD4";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();


        const t = Math.min(0.9, Math.max(0.1, edge.labelT === undefined ? 0.5 : edge.labelT));
        const dx = x2 - x1;
        const dy = y2 - y1;
        const length = Math.max(1, Math.sqrt(dx * dx + dy * dy));

        let nx = -dy / length;
        let ny = dx / length;

        if (ny > 0) {
            nx = -nx;
            ny = -ny;
        }

        if (Math.abs(dx) < 8) {
            nx = 1;
            ny = 0;
        }

        const offset = Math.abs(dx) < 8 ? 42 : 18;
        const labelX = x1 + dx * t + nx * offset;
        const labelY = y1 + dy * t + ny * offset;

        if (!edge.label) { continue; }
        ctx.font = '400 12px "Microsoft YaHei", sans-serif';
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";

        const textWidth = ctx.measureText(edge.label).width;

        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(labelX - textWidth / 2 - 6, labelY - 9, textWidth + 12, 18);

        ctx.fillStyle = "#4527A0";
        ctx.fillText(edge.label, labelX, labelY);
    }

    // ---- 节点 ----
    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];

        const x = nodeLeft(node);
        const y = nodeTop(node);
        const h = node._height;

        ctx.fillStyle = node.isResult ? "#FAF8FF" : "#FFFFFF";
        ctx.strokeStyle = node.isResult ? "#512BD4" : (node.isRef ? "#B9A9EE" : "#D0D0D0");
        ctx.lineWidth = node.isResult ? 2 : 1.2;

        ctx.beginPath();
        ctx.roundRect(x, y, node._w || NODE_W, h, 12);
        ctx.fill();
        ctx.stroke();

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

        // 与界面一致：按节点框实时绘制表格（c/S/P 字母 + 底色）
        if (node.state && node.state.cells) {
            const columns = node.state.columns;
            const rows = node.state.rows;
            const cellW = 30;
            const cellH = 30;

            const left = x + ((node._w || NODE_W) - columns * 30) / 2;
            const top = y + HEADER_H;

            for (let row = 0; row < rows; row++) {
                for (let column = 0; column < columns; column++) {
                    const cell = node.state.cells[row * columns + column] || {};
                    const cx = left + column * cellW;
                    const cy = top + row * cellH;

                    if (cell.type && cell.type !== "empty") {
                        ctx.fillStyle = cell.type === "crystal"
                            ? "#FF2D55"
                            : (cell.colorCode ? "#4A90D9" : (cell.type === "pin" ? "#1A1A1A" : "#2B2B2B"));

                        ctx.fillRect(cx + cellW * 0.08, cy + cellH * 0.08, cellW * 0.84, cellH * 0.84);

                        if (cellW >= 8 && cellH >= 8) {
                            ctx.fillStyle = "#FFFFFF";
                            ctx.font = '700 ' + Math.round(Math.min(cellW, cellH) * 0.55) + 'px "Microsoft YaHei", sans-serif';
                            ctx.textAlign = "center";
                            ctx.textBaseline = "middle";
                            ctx.fillText(
                                cell.type === "crystal" ? "c" : (cell.type === "pin" ? "P" : "S"),
                                cx + cellW / 2,
                                cy + cellH / 2);
                        }
                    }

                    ctx.strokeStyle = "#E6E2F2";
                    ctx.lineWidth = 0.6;
                    ctx.strokeRect(cx, cy, cellW, cellH);
                }
            }
        }
    }

    // 箭头单独一遍绘制，避免被后绘制的节点卡片遮住
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.from];
        const to = byId[edge.to];

        if (!from || !to) { continue; }

        const a1 = edgeAnchor(from, to);
        const a2 = edgeAnchor(to, from);

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        const tipX = (edgeGeom[i] ? edgeGeom[i].x : a2.x) - vx * 3;
        const tipY = (edgeGeom[i] ? edgeGeom[i].y : a2.y) - vy * 3;

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(bx - vy * half, by + vx * half);
        ctx.lineTo(bx + vy * half, by - vx * half);
        ctx.closePath();
        ctx.fillStyle = "#512BD4";
        ctx.fill();
    }
    const blob = await canvasToBlob(canvas);
    if (await window.shapeGridTryShare(blob, fileName)) { return; }
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);
    window.shapeGridSaveOrShare(link, fileName);
    link.remove();

    URL.revokeObjectURL(url);
};
window.shapeUi = {
    focus: function (element) {
        try {
            element.focus();
            element.select();
        } catch (e) {
        }
    }
};
/*
 * 移动端优先调起系统分享（保存到相册/发送给应用），
 * 不支持 Web Share 时才退回浏览器下载。
 */
window.shapeGridSaveOrShare = async function (link, fileName) {
    try {
        if (navigator.canShare &&
            link.href &&
            link.href.indexOf("blob:") === 0) {
            const response = await fetch(link.href);
            const blob = await response.blob();
            const file = new File([blob], fileName, {
                type: blob.type || "image/png"
            });

            if (navigator.canShare({ files: [file] })) {
                await navigator.share({ files: [file], title: fileName });

                URL.revokeObjectURL(link.href);
                link.remove();

                return;
            }
        }
    } catch (error) {
        /* 用户取消分享或环境不支持，继续走下载 */
    }

    link.click();
};
/*
 * 直接拿现有 blob 调系统分享：不再 fetch，避免丢失用户手势
 * 导致 navigator.share 抛 NotAllowedError 后回落成下载。
 */
window.shapeGridTryShare = async function (blob, fileName) {
    try {
        if (!navigator.canShare || !blob) {
            return false;
        }

        const file = new File([blob], fileName, {
            type: blob.type || "image/png"
        });

        if (!navigator.canShare({ files: [file] })) {
            return false;
        }

        await navigator.share({ files: [file], title: fileName });

        return true;
    } catch (error) {
        return false;
    }
};
/*
 * 分析图导出（单栏版式）。
 *
 * payload 结构见 Models/AnalysisExportPayload.cs：
 * { title, code, state, sections: [{ title, lines, table, isPlaceholder }], meta, generatedAt }
 * section.table = { headers: [...], rows: [[...], ...] } 时按等宽列表格绘制，
 * 否则（或同时还有 lines 时先画文本行）沿用文本行绘制。
 * 页面只会把当前显示的板块放进 sections，隐藏的板块不会出现在图片里。
 */
window.shapeGrid.downloadAnalysis = async function (payload, fileName) {
    const PADDING = 32;
    const TOP_BAR = 6;
    const SECTION_GAP = 22;
    const SECTION_TITLE_HEIGHT = 30;
    const LINE_HEIGHT = 22;
    const MAX_SHAPE_WIDTH = 760;
    const MIN_CONTENT_WIDTH = 720;
    const MAX_SIDE = 16000;

    const TITLE_FONT = '700 30px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    const CODE_FONT = '400 13px Consolas, "Courier New", monospace';
    const SECTION_FONT = '700 18px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    const TEXT_FONT = '400 14px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    const FOOTER_FONT = '400 12px "Microsoft YaHei", "Noto Sans SC", sans-serif';

    const data = payload || {};
    const state = data.state || null;

    // 竖式图尺寸：按列数缩放到最大宽度，避免宽图形把单栏版面撑爆。
    const shapeTheme = resolveTheme(state);
    const baseCellWidth = shapeTheme.exportCellWidth || 128;
    const baseCellHeight = shapeTheme.exportCellHeight || 96;
    const hasShape = !!state && state.columns > 0 && state.rows > 0;
    const shapeScale = hasShape
        ? Math.min(1, MAX_SHAPE_WIDTH / (state.columns * baseCellWidth))
        : 1;
    const cellWidth = baseCellWidth * shapeScale;
    const cellHeight = baseCellHeight * shapeScale;
    const shapeWidth = hasShape ? state.columns * cellWidth : 0;
    const shapeHeight = hasShape ? state.rows * cellHeight : 0;

    const sections = Array.isArray(data.sections) ? data.sections : [];

    // 制作路径节点图按实际尺寸整张绘制；这里先把整张导出图撑到节点图宽度。
    let graphContentWidth = 0;

    for (let i = 0; i < sections.length; i++) {
        const graph = sections[i] && sections[i].graph;

        if (graph) {
            graphContentWidth = Math.max(
                graphContentWidth,
                Number(graph.width) || 0);
        }
    }

    const contentWidth = Math.max(
        MIN_CONTENT_WIDTH,
        shapeWidth,
        graphContentWidth);

    // 先用离屏画布量文本，排版高度确定后再创建正式画布。
    const measureCanvas = document.createElement("canvas");
    const measure = measureCanvas.getContext("2d");

    if (!measure) {
        throw new Error("无法创建画布。");
    }

    const titleLines = wrapAnalysisText(
        measure,
        data.title || "图形分析",
        TITLE_FONT,
        contentWidth,
        false);

    const codeLines = data.code
        ? wrapAnalysisText(measure, data.code, CODE_FONT, contentWidth, true)
        : [];

    const headerHeight =
        titleLines.length * 40 +
        (codeLines.length > 0 ? codeLines.length * 19 + 6 : 0) +
        20;

    const blocks = [
        {
            kind: "header",
            height: headerHeight,
            titleLines: titleLines,
            codeLines: codeLines
        }
    ];

    if (hasShape) {
        blocks.push({
            kind: "shape",
            height: shapeHeight + 26
        });
    }

    for (let i = 0; i < sections.length; i++) {
        const section = sections[i] || {};
        const title = String(section.title || "");

        if (section.isPlaceholder) {
            const text = Array.isArray(section.lines) && section.lines.length > 0
                ? String(section.lines[0])
                : "制作路径（待接入）";

            blocks.push({
                kind: "placeholder",
                height: SECTION_TITLE_HEIGHT + 64 + SECTION_GAP,
                title: title,
                text: text
            });

            continue;
        }

        const lines = [];
        const rawLines = Array.isArray(section.lines) ? section.lines : [];

        for (let j = 0; j < rawLines.length; j++) {
            const wrapped = wrapAnalysisText(
                measure,
                rawLines[j],
                TEXT_FONT,
                contentWidth,
                false);

            for (let k = 0; k < wrapped.length; k++) {
                lines.push(wrapped[k]);
            }
        }

        // 制作路径节点图：标题与说明行照常绘制，下面按实际尺寸整张画图。
        if (section.graph) {
            blocks.push({
                kind: "graph",
                height:
                    SECTION_TITLE_HEIGHT +
                    lines.length * LINE_HEIGHT +
                    (Number(section.graph.height) || 0) +
                    SECTION_GAP,
                title: title,
                lines: lines,
                graph: section.graph
            });

            continue;
        }

        const table = normalizeAnalysisTable(section.table);

        if (table) {
            // 先画 lines（汇总/说明），再画表格；表格高度按行数算，文本宽度在绘制时截断。
            blocks.push({
                kind: "table",
                height:
                    SECTION_TITLE_HEIGHT +
                    lines.length * LINE_HEIGHT +
                    analysisTableHeight(table) +
                    SECTION_GAP,
                title: title,
                lines: lines,
                table: table
            });

            continue;
        }

        if (lines.length === 0) {
            lines.push("—");
        }

        blocks.push({
            kind: "text",
            height: SECTION_TITLE_HEIGHT + lines.length * LINE_HEIGHT + SECTION_GAP,
            title: title,
            lines: lines
        });
    }

    // 底部：分隔线 + 层数/象限数 + 生成时间。
    blocks.push({
        kind: "footer",
        height: 55
    });

    let contentHeight = 0;

    for (let i = 0; i < blocks.length; i++) {
        contentHeight += blocks[i].height;
    }

    const width = contentWidth + PADDING * 2;
    const height = TOP_BAR + PADDING * 2 + contentHeight;

    if (width > MAX_SIDE || height > MAX_SIDE) {
        throw new Error("图片尺寸过大：" + width + " × " + height);
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;

    const ctx = canvas.getContext("2d");

    if (!ctx) {
        throw new Error("无法创建画布。");
    }

    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, width, height);

    // 顶部主色装饰条，与站点的 #512bd4 主色一致。
    ctx.fillStyle = "#512BD4";
    ctx.fillRect(0, 0, width, TOP_BAR);

    let y = TOP_BAR + PADDING;

    function drawSectionTitle(title, top) {
        ctx.fillStyle = "#512BD4";
        ctx.fillRect(PADDING, top + 5, 4, 18);

        ctx.font = SECTION_FONT;
        ctx.fillStyle = "#222222";
        ctx.textAlign = "left";
        ctx.textBaseline = "top";
        ctx.fillText(title, PADDING + 12, top + 3);
    }

    for (let i = 0; i < blocks.length; i++) {
        const block = blocks[i];

        if (block.kind === "header") {
            ctx.textAlign = "center";
            ctx.textBaseline = "top";

            ctx.font = TITLE_FONT;
            ctx.fillStyle = "#222222";

            for (let j = 0; j < block.titleLines.length; j++) {
                ctx.fillText(block.titleLines[j], width / 2, y);
                y += 40;
            }

            if (block.codeLines.length > 0) {
                y += 6;

                ctx.font = CODE_FONT;
                ctx.fillStyle = "#4527A0";

                for (let j = 0; j < block.codeLines.length; j++) {
                    ctx.fillText(block.codeLines[j], width / 2, y);
                    y += 19;
                }
            }

            y += 20;
            continue;
        }

        if (block.kind === "shape") {
            const shapeLeft = PADDING + (contentWidth - shapeWidth) / 2;

            ctx.strokeStyle = "#E6E2F2";
            ctx.lineWidth = 1;
            ctx.strokeRect(shapeLeft - 1, y - 1, shapeWidth + 2, shapeHeight + 2);

            window.shapeGrid.drawAnalysisShape(
                ctx,
                state,
                shapeLeft,
                y,
                cellWidth,
                cellHeight);

            y += block.height;
            continue;
        }

        if (block.kind === "text") {
            drawSectionTitle(block.title, y);

            ctx.font = TEXT_FONT;
            ctx.fillStyle = "#3A3A44";
            ctx.textAlign = "left";
            ctx.textBaseline = "top";

            let lineY = y + SECTION_TITLE_HEIGHT;

            for (let j = 0; j < block.lines.length; j++) {
                if (block.lines[j] !== "") {
                    ctx.fillText(block.lines[j], PADDING, lineY);
                }

                lineY += LINE_HEIGHT;
            }

            // 板块之间的细分隔线
            ctx.strokeStyle = "#EEEAF7";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(PADDING, lineY + 6);
            ctx.lineTo(width - PADDING, lineY + 6);
            ctx.stroke();

            y += block.height;
            continue;
        }

        if (block.kind === "table") {
            drawSectionTitle(block.title, y);

            ctx.font = TEXT_FONT;
            ctx.fillStyle = "#3A3A44";
            ctx.textAlign = "left";
            ctx.textBaseline = "top";

            let lineY = y + SECTION_TITLE_HEIGHT;

            for (let j = 0; j < block.lines.length; j++) {
                if (block.lines[j] !== "") {
                    ctx.fillText(block.lines[j], PADDING, lineY);
                }

                lineY += LINE_HEIGHT;
            }

            drawAnalysisTable(ctx, block.table, PADDING, lineY, contentWidth);

            const tableBottom = lineY + analysisTableHeight(block.table);

            // 板块之间的细分隔线
            ctx.strokeStyle = "#EEEAF7";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(PADDING, tableBottom + 6);
            ctx.lineTo(width - PADDING, tableBottom + 6);
            ctx.stroke();

            y += block.height;
            continue;
        }

        if (block.kind === "graph") {
            drawSectionTitle(block.title, y);

            ctx.font = TEXT_FONT;
            ctx.fillStyle = "#3A3A44";
            ctx.textAlign = "left";
            ctx.textBaseline = "top";

            let lineY = y + SECTION_TITLE_HEIGHT;

            for (let j = 0; j < block.lines.length; j++) {
                if (block.lines[j] !== "") {
                    ctx.fillText(block.lines[j], PADDING, lineY);
                }

                lineY += LINE_HEIGHT;
            }

            const graph = block.graph || {};
            const graphWidth = Number(graph.width) || 0;
            const graphLeft = PADDING + Math.max(0, (contentWidth - graphWidth) / 2);

            drawAnalysisPathGraph(ctx, graph, graphLeft, lineY);

            const graphBottom = lineY + (Number(graph.height) || 0);

            // 板块之间的细分隔线
            ctx.strokeStyle = "#EEEAF7";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(PADDING, graphBottom + 6);
            ctx.lineTo(width - PADDING, graphBottom + 6);
            ctx.stroke();

            y += block.height;
            continue;
        }

        if (block.kind === "placeholder") {
            drawSectionTitle(block.title, y);

            const boxTop = y + SECTION_TITLE_HEIGHT;
            const boxHeight = 64;

            ctx.save();
            ctx.setLineDash([6, 4]);
            ctx.fillStyle = "#FAF8FF";
            ctx.strokeStyle = "#B9A9EE";
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.roundRect(PADDING, boxTop, contentWidth, boxHeight, 10);
            ctx.fill();
            ctx.stroke();
            ctx.restore();

            ctx.font = '600 15px "Microsoft YaHei", "Noto Sans SC", sans-serif';
            ctx.fillStyle = "#512BD4";
            ctx.textAlign = "center";
            ctx.textBaseline = "middle";
            ctx.fillText(block.text, width / 2, boxTop + boxHeight / 2);

            y += block.height;
            continue;
        }

        if (block.kind === "footer") {
            y += 20;

            ctx.strokeStyle = "#E6E2F2";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(PADDING, y);
            ctx.lineTo(width - PADDING, y);
            ctx.stroke();

            y += 16;

            ctx.font = FOOTER_FONT;
            ctx.fillStyle = "#6B6B76";
            ctx.textBaseline = "top";

            ctx.textAlign = "left";
            ctx.fillText(data.meta || "", PADDING, y);

            ctx.textAlign = "right";
            ctx.fillText(data.generatedAt || "", width - PADDING, y);
        }
    }

    const blob = await canvasToBlob(canvas);

    if (await window.shapeGridTryShare(blob, fileName)) {
        return;
    }

    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";

    document.body.appendChild(link);

    // 等分享/下载流程结束后再释放 blob URL，避免移动端 fetch 时链接已失效。
    await window.shapeGridSaveOrShare(link, fileName);

    link.remove();

    URL.revokeObjectURL(url);
};

/*
 * 在指定位置绘制分析用的竖式图。
 * 复用 shape-grid.js 已有的 drawCell / drawGrid，保持与编辑器一致的配色。
 */
window.shapeGrid.drawAnalysisShape = function (
    ctx,
    state,
    left,
    top,
    cellWidth,
    cellHeight) {
    if (!ctx ||
        !state ||
        !state.cells ||
        state.columns <= 0 ||
        state.rows <= 0) {
        return;
    }

    const theme = resolveTheme(state);

    ctx.save();
    ctx.translate(left, top);

    for (let row = 0; row < state.rows; row++) {
        for (let column = 0; column < state.columns; column++) {
            const cell =
                state.cells[row * state.columns + column] ||
                { type: "empty" };

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

    ctx.restore();
};

/*
 * 制作路径节点里的固定尺寸预览：按 30px 单元格 1:1 绘制，不做缩放。
 * 复用 drawAnalysisShape 的配色与字形（c / S / P + 颜色）。
 */
window.shapeGrid.renderAnalysisPreview = function (canvas, state, cellSize) {
    if (!canvas ||
        !state ||
        !state.cells ||
        state.columns <= 0 ||
        state.rows <= 0) {
        return;
    }

    const size = cellSize > 0 ? cellSize : 30;

    const width = state.columns * size;
    const height = state.rows * size;

    // CSS 尺寸固定为单元格尺寸总和，画布后备缓冲按 DPR 放大保持清晰。
    canvas.style.width = width + "px";
    canvas.style.height = height + "px";

    const dpr = window.devicePixelRatio || 1;

    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);

    const ctx = canvas.getContext("2d");

    if (!ctx) {
        return;
    }

    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);

    window.shapeGrid.drawAnalysisShape(
        ctx,
        state,
        0,
        0,
        size,
        size);
};

/*
 * 分析页左侧信息区的自适应预览。
 *
 * 以 30px 单元格为 1:1 基准，等比缩小到父容器可用宽度，
 * 高度上限 maxHeight（默认 320px）；scale 不超过 1，因此小图形不会被放大。
 * 画布后备缓冲按 DPR 放大，CSS 尺寸按缩放后的逻辑尺寸设置。
 */
window.shapeGrid.renderAnalysisFit = function (canvas, state, cellSize, maxHeight) {
    if (!canvas ||
        !state ||
        !state.cells ||
        state.columns <= 0 ||
        state.rows <= 0) {
        return;
    }

    const size = cellSize > 0 ? cellSize : 30;
    const maxHeightValue = maxHeight > 0 ? maxHeight : 320;

    let attempts = 0;

    function draw() {
        if (!canvas.isConnected) {
            return;
        }

        const parent = canvas.parentElement;
        const availableWidth = parent ? parent.clientWidth : canvas.clientWidth;

        if (!(availableWidth > 0)) {
            attempts++;

            if (attempts < 20) {
                window.requestAnimationFrame(draw);
            }

            return;
        }

        const naturalWidth = state.columns * size;
        const naturalHeight = state.rows * size;

        // 只缩小不放大：scale 上限固定为 1。
        const scale = Math.min(
            1,
            availableWidth / naturalWidth,
            maxHeightValue / naturalHeight);

        const width = Math.max(1, Math.floor(naturalWidth * scale));
        const height = Math.max(1, Math.floor(naturalHeight * scale));

        canvas.style.width = width + "px";
        canvas.style.height = height + "px";

        const dpr = window.devicePixelRatio || 1;

        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);

        const ctx = canvas.getContext("2d");

        if (!ctx) {
            return;
        }

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, width, height);

        // 直接按缩放后的单元格尺寸绘制，避免 CSS 二次拉伸导致模糊。
        window.shapeGrid.drawAnalysisShape(
            ctx,
            state,
            0,
            0,
            width / state.columns,
            height / state.rows);
    }

    canvas._analysisFitDraw = draw;

    // 父容器宽度变化时重新适配；只关心宽度，避免高度变化触发自身循环。
    if (!canvas._analysisFitObserver &&
        typeof window.ResizeObserver === "function") {
        let lastWidth = -1;

        const observer = new window.ResizeObserver(function () {
            const parent = canvas.parentElement;
            const width = parent ? parent.clientWidth : 0;

            if (width !== lastWidth) {
                lastWidth = width;

                if (typeof canvas._analysisFitDraw === "function") {
                    canvas._analysisFitDraw();
                }
            }
        });

        canvas._analysisFitObserver = observer;

        if (canvas.parentElement) {
            observer.observe(canvas.parentElement);
        }
    }

    draw();
};

/* 组件卸载时断开观察器，避免页面跳转后留下引用。 */
window.shapeGrid.disposeAnalysisFit = function (canvas) {
    if (!canvas) {
        return;
    }

    if (canvas._analysisFitObserver) {
        canvas._analysisFitObserver.disconnect();
        canvas._analysisFitObserver = null;
    }

    canvas._analysisFitDraw = null;
};

/*
 * 分析导出图里的制作路径节点图。
 *
 * 坐标来自 C# 的分层布局，节点尺寸与页面一致：
 * 宽 = 象限数 × 30 + 16，高 = 50 + 层数 × 30。
 * 预览固定 30px 单元格、1:1 绘制；图有多宽就画多宽，不缩放、不分片。
 * 废料节点淡显、虚线边框，指向废料的箭头为灰色虚线。
 */
function drawAnalysisPathGraph(ctx, graph, left, top) {
    const nodes = Array.isArray(graph.nodes) ? graph.nodes : [];
    const edges = Array.isArray(graph.edges) ? graph.edges : [];

    if (nodes.length === 0) {
        return;
    }

    const byId = {};

    for (let i = 0; i < nodes.length; i++) {
        byId[nodes[i].id] = nodes[i];
    }

    function rectOf(node) {
        return {
            x: left + node.x,
            y: top + node.y,
            width: node.width,
            height: node.height
        };
    }

    /*
     * 分层图锚点：跨层走「上排底边中点 → 下排顶边中点」，
     * 同层（交换 → 废料）走相对的侧边中点。
     */
    function edgeAnchors(node, other) {
        const a = rectOf(node);
        const b = rectOf(other);

        if (b.y >= a.y + a.height - 0.5) {
            return {
                start: { x: a.x + a.width / 2, y: a.y + a.height },
                end: { x: b.x + b.width / 2, y: b.y }
            };
        }

        if (a.y >= b.y + b.height - 0.5) {
            return {
                start: { x: a.x + a.width / 2, y: a.y },
                end: { x: b.x + b.width / 2, y: b.y + b.height }
            };
        }

        const rightward = b.x >= a.x;
        const aY = a.y + a.height / 2;
        const bY = b.y + b.height / 2;

        return rightward
            ? {
                start: { x: a.x + a.width, y: aY },
                end: { x: b.x, y: bY }
            }
            : {
                start: { x: a.x, y: aY },
                end: { x: b.x + b.width, y: bY }
            };
    }

    const TITLE_FONT = '600 11px "Microsoft YaHei", "Noto Sans SC", sans-serif';
    const NODE_FONT = '400 12px "Microsoft YaHei", "Noto Sans SC", sans-serif';

    // 先画卡片，箭头随后画在最上层，避免被卡片遮住。
    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];
        const r = rectOf(node);

        if (node.isWaste) {
            ctx.fillStyle = "#FBFBFC";
            ctx.strokeStyle = "#C4C4CC";
        }
        else {
            ctx.fillStyle = node.isResult ? "#FAF8FF" : "#FFFFFF";
            ctx.strokeStyle = node.isResult ? "#512BD4" : "#D0D0D0";
        }

        ctx.lineWidth = node.isResult ? 2 : 1.2;

        ctx.save();

        if (node.isWaste) {
            ctx.setLineDash([5, 4]);
        }

        ctx.beginPath();
        ctx.roundRect(r.x, r.y, r.width, r.height, 12);
        ctx.fill();
        ctx.stroke();

        ctx.restore();

        // 标题最多两行，与页面卡片一致
        ctx.font = TITLE_FONT;
        ctx.fillStyle = node.isWaste ? "#9A9AA5" : "#4A3F72";
        ctx.textAlign = "left";
        ctx.textBaseline = "top";

        const titleLines = wrapAnalysisText(
            ctx,
            node.title || "",
            TITLE_FONT,
            Math.max(20, r.width - 14),
            false);

        for (let line = 0; line < Math.min(2, titleLines.length); line++) {
            let text = titleLines[line];

            if (line === 1 && titleLines.length > 2) {
                text += "…";
            }

            ctx.fillText(text, r.x + 7, r.y + 7 + line * 13);
        }

        // 预览：与页面相同的 30px 单元格，不做缩放；废料降低对比度
        if (node.state) {
            if (node.isWaste) {
                ctx.save();
                ctx.globalAlpha = 0.55;
            }

            window.shapeGrid.drawAnalysisShape(
                ctx,
                node.state,
                r.x + 8,
                r.y + 37,
                30,
                30);

            if (node.isWaste) {
                ctx.restore();
            }
        }
        else if (node.isPending) {
            ctx.font = NODE_FONT;
            ctx.fillStyle = "#8B7BBF";
            ctx.textAlign = "center";
            ctx.textBaseline = "middle";
            ctx.fillText(
                "待接入",
                r.x + r.width / 2,
                r.y + 37 + (r.height - 37) / 2);
        }
    }

    // 箭头：从节点边缘出发，指向下一个节点边缘
    for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const from = byId[edge.fromId];
        const to = byId[edge.toId];

        if (!from || !to) {
            continue;
        }

        const anchors = edgeAnchors(from, to);
        const a1 = anchors.start;
        const a2 = anchors.end;

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        const tipX = a2.x + vx * 1;
        const tipY = a2.y + vy * 1;

        ctx.save();

        if (edge.isWaste) {
            ctx.strokeStyle = "#9A9AA5";
            ctx.fillStyle = "#9A9AA5";
            ctx.setLineDash([5, 4]);
        }
        else {
            ctx.strokeStyle = "#512BD4";
            ctx.fillStyle = "#512BD4";
        }

        ctx.lineWidth = 2;

        ctx.beginPath();
        ctx.moveTo(a1.x - vx * 1, a1.y - vy * 1);
        ctx.lineTo(tipX, tipY);
        ctx.stroke();

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(bx - vy * half, by + vx * half);
        ctx.lineTo(bx + vy * half, by - vx * half);
        ctx.closePath();
        ctx.fill();

        ctx.restore();
    }
}

/*
 * 文本换行：中文按字拆，英文/数字/公式按词拆，
 * 避免长方向列表或短代码把画布撑出边界。
 */
function wrapAnalysisText(
    measure,
    text,
    font,
    maxWidth,
    breakAll) {
    const value =
        text === null ||
            text === undefined
            ? ""
            : String(text);

    if (!value) {
        return [""];
    }

    measure.font = font;

    const tokens =
        breakAll
            ? Array.from(value)
            : tokenizeAnalysisText(value);

    const lines = [];

    let line = "";

    for (let i = 0; i < tokens.length; i++) {
        const token = tokens[i];

        if (token === "\n") {
            lines.push(line);
            line = "";
            continue;
        }

        const candidate = line + token;

        if (line !== "" &&
            measure.measureText(candidate).width > maxWidth) {
            lines.push(line.replace(/\s+$/, ""));
            line = token.replace(/^\s+/, "");
        }
        else {
            line = candidate;
        }
    }

    lines.push(line);

    return lines;
}

function tokenizeAnalysisText(text) {
    const tokens = [];

    let word = "";

    function flush() {
        if (word !== "") {
            tokens.push(word);
            word = "";
        }
    }

    for (const ch of Array.from(text)) {
        if (/[A-Za-z0-9_+\-./#:()=]/.test(ch)) {
            word += ch;
        }
        else if (/\s/.test(ch)) {
            flush();
            tokens.push(" ");
        }
        else {
            flush();
            tokens.push(ch);
        }
    }

    flush();

    return tokens;
}

/*
 * 分析导出表格的版式：行高 22px、字号 12px，中文沿用同一套字体栈。
 * 表格列等宽，列宽 = 画布内容宽度 / 列数，文本过长时截断并加省略号。
 */
const ANALYSIS_TABLE_ROW_HEIGHT = 22;

const ANALYSIS_TABLE_FONT = '400 12px "Microsoft YaHei", "Noto Sans SC", sans-serif';

const ANALYSIS_TABLE_HEADER_FONT = '700 12px "Microsoft YaHei", "Noto Sans SC", sans-serif';

/* 表格总高度：表头一行 + 表体若干行，末尾留 8px 给板块分隔线。 */
function analysisTableHeight(table) {
    const bodyRows = table && Array.isArray(table.rows) ? table.rows.length : 0;

    return (1 + bodyRows) * ANALYSIS_TABLE_ROW_HEIGHT + 8;
}

/* 把 section.table 归一化成 { headers: string[], rows: string[][] }，空表格返回 null。 */
function normalizeAnalysisTable(table) {
    if (!table || typeof table !== "object") {
        return null;
    }

    const headers = Array.isArray(table.headers)
        ? table.headers.map(function (value) {
            return value === null || value === undefined ? "" : String(value);
        })
        : [];

    const rows = [];

    if (Array.isArray(table.rows)) {
        for (let i = 0; i < table.rows.length; i++) {
            const rawRow = table.rows[i];

            rows.push(
                Array.isArray(rawRow)
                    ? rawRow.map(function (value) {
                        return value === null || value === undefined ? "" : String(value);
                    })
                    : []);
        }
    }

    if (headers.length === 0 && rows.length === 0) {
        return null;
    }

    return { headers: headers, rows: rows };
}

/*
 * 单元格文本截断：超出列宽时从尾部删字并补一个省略号。
 * 用 Array.from 拆字，避免把中文/emoji 的代理对截成半个。
 */
function truncateAnalysisText(ctx, text, font, maxWidth) {
    const value = text === null || text === undefined ? "" : String(text);

    if (!value || maxWidth <= 0) {
        return "";
    }

    ctx.font = font;

    if (ctx.measureText(value).width <= maxWidth) {
        return value;
    }

    const chars = Array.from(value);
    const ellipsis = "…";

    let length = chars.length;

    while (length > 0 &&
        ctx.measureText(chars.slice(0, length).join("") + ellipsis).width > maxWidth) {
        length--;
    }

    return length > 0
        ? chars.slice(0, length).join("") + ellipsis
        : "";
}

/*
 * 在画布上画一个等宽列表格：表头底色 + 行间分隔线 + 外框。
 * 表格左边界和宽度由调用方给出，不含标题和说明文本。
 */
function drawAnalysisTable(ctx, table, left, top, tableWidth) {
    const headers = table.headers;
    const rows = table.rows;

    let columnCount = headers.length;

    for (let i = 0; i < rows.length; i++) {
        if (rows[i].length > columnCount) {
            columnCount = rows[i].length;
        }
    }

    if (columnCount === 0) {
        columnCount = 1;
    }

    const columnWidth = tableWidth / columnCount;
    const cellPadding = 8;
    const textWidth = Math.max(0, columnWidth - cellPadding * 2);
    const rowMiddle = ANALYSIS_TABLE_ROW_HEIGHT / 2;

    let rowTop = top;

    // 表头：淡紫底色，与站点主色 #512bd4 同一色系。
    ctx.fillStyle = "#F3F0FC";
    ctx.fillRect(left, rowTop, tableWidth, ANALYSIS_TABLE_ROW_HEIGHT);

    ctx.font = ANALYSIS_TABLE_HEADER_FONT;
    ctx.fillStyle = "#4A3F72";
    ctx.textAlign = "left";
    ctx.textBaseline = "middle";

    for (let column = 0; column < columnCount; column++) {
        const text = truncateAnalysisText(
            ctx,
            headers[column] || "",
            ANALYSIS_TABLE_HEADER_FONT,
            textWidth);

        ctx.fillText(
            text,
            left + column * columnWidth + cellPadding,
            rowTop + rowMiddle);
    }

    rowTop += ANALYSIS_TABLE_ROW_HEIGHT;

    // 表体：隔行浅色底 + 每行下方一条分隔线。
    ctx.font = ANALYSIS_TABLE_FONT;
    ctx.textAlign = "left";
    ctx.textBaseline = "middle";

    for (let row = 0; row < rows.length; row++) {
        if (row % 2 === 1) {
            ctx.fillStyle = "#FBFAFE";
            ctx.fillRect(left, rowTop, tableWidth, ANALYSIS_TABLE_ROW_HEIGHT);
        }

        ctx.fillStyle = "#3A3A44";

        for (let column = 0; column < columnCount; column++) {
            const text = truncateAnalysisText(
                ctx,
                rows[row][column] || "",
                ANALYSIS_TABLE_FONT,
                textWidth);

            ctx.fillText(
                text,
                left + column * columnWidth + cellPadding,
                rowTop + rowMiddle);
        }

        rowTop += ANALYSIS_TABLE_ROW_HEIGHT;

        ctx.strokeStyle = "#EEEAF7";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(left, rowTop);
        ctx.lineTo(left + tableWidth, rowTop);
        ctx.stroke();
    }

    // 外框：让表格与上下文本行区分开。
    ctx.strokeStyle = "#E6E2F2";
    ctx.lineWidth = 1;
    ctx.strokeRect(left, top, tableWidth, (1 + rows.length) * ANALYSIS_TABLE_ROW_HEIGHT);
}

/* 供 Blazor 回读节点的真实渲染尺寸，箭头锚点据此贴合边框 */
window.shapeGridMeasureNodes = function () {
    return Array.from(document.querySelectorAll(".graph-node[data-node-id]")).map(function (el) {
        const rect = el.getBoundingClientRect();

        return { id: el.getAttribute("data-node-id"), height: rect.height, width: rect.width };
    });
};
/*
 * 连线几何全部在浏览器里算：以节点真实矩形为唯一数据源，
 * 避免 C# 计算与渲染结果不一致导致的箭头偏移。
 *
 * layered = true 时用于分析页的分层制作路径图：
 * 跨层箭头走「上排底边中点 → 下排顶边中点」，同层箭头走相对侧边中点；
 * 其余页面（工作区）仍使用中心连心线与矩形边界的交点。
 */
window.shapeGridLayoutEdges = function (layered) {
    const canvas = document.querySelector(".graph-canvas");
    if (!canvas) { return; }

    const canvasRect = canvas.getBoundingClientRect();
    const nodes = {};

    canvas.querySelectorAll(".graph-node[data-node-id]").forEach(function (el) {
        const rect = el.getBoundingClientRect();

        nodes[el.getAttribute("data-node-id")] = {
            left: rect.left - canvasRect.left,
            top: rect.top - canvasRect.top,
            width: rect.width,
            height: rect.height
        };
    });

    function anchor(node, other) {
        const cx = node.left + node.width / 2;
        const cy = node.top + node.height / 2;
        const ox = other.left + other.width / 2;
        const oy = other.top + other.height / 2;

        let dx = ox - cx;
        let dy = oy - cy;
        const len = Math.sqrt(dx * dx + dy * dy);

        if (len < 1) { return { x: cx, y: cy }; }

        dx /= len;
        dy /= len;

        const sx = Math.abs(dx) < 1e-6 ? Number.MAX_VALUE : (node.width / 2) / Math.abs(dx);
        const sy = Math.abs(dy) < 1e-6 ? Number.MAX_VALUE : (node.height / 2) / Math.abs(dy);
        const scale = Math.min(sx, sy);

        return { x: cx + dx * scale, y: cy + dy * scale };
    }

    function layeredAnchors(from, to) {
        if (to.top >= from.top + from.height - 0.5) {
            return {
                start: { x: from.left + from.width / 2, y: from.top + from.height },
                end: { x: to.left + to.width / 2, y: to.top }
            };
        }

        if (from.top >= to.top + to.height - 0.5) {
            return {
                start: { x: from.left + from.width / 2, y: from.top },
                end: { x: to.left + to.width / 2, y: to.top + to.height }
            };
        }

        const rightward = to.left >= from.left;
        const fromY = from.top + from.height / 2;
        const toY = to.top + to.height / 2;

        return rightward
            ? {
                start: { x: from.left + from.width, y: fromY },
                end: { x: to.left, y: toY }
            }
            : {
                start: { x: from.left, y: fromY },
                end: { x: to.left + to.width, y: toY }
            };
    }

    canvas.querySelectorAll("path[data-edge-from]").forEach(function (path) {
        const from = nodes[path.getAttribute("data-edge-from")];
        const to = nodes[path.getAttribute("data-edge-to")];

        if (!from || !to) { return; }

        let a1;
        let a2;

        if (layered) {
            const anchors = layeredAnchors(from, to);
            a1 = anchors.start;
            a2 = anchors.end;
        }
        else {
            a1 = anchor(from, to);
            a2 = anchor(to, from);
        }

        let vx = a2.x - a1.x;
        let vy = a2.y - a1.y;
        const len = Math.max(1, Math.sqrt(vx * vx + vy * vy));

        vx /= len;
        vy /= len;

        // 两端各向外延伸 1px，抵消 2px 描边的一半，视觉上正好贴住边框
        const tipX = a2.x + vx * 1;
        const tipY = a2.y + vy * 1;

        const startX = a1.x - vx * 1;
        const startY = a1.y - vy * 1;

        path.setAttribute("d", "M " + startX + " " + startY + " L " + tipX + " " + tipY);

        const head = 11;
        const half = 5;
        const bx = tipX - vx * head;
        const by = tipY - vy * head;

        const polygon = canvas.querySelector(
            'polygon[data-edge-from="' + path.getAttribute("data-edge-from") +
            '"][data-edge-to="' + path.getAttribute("data-edge-to") + '"]');

        if (polygon) {
            polygon.setAttribute("points",
                tipX + "," + tipY + " " +
                (bx - vy * half) + "," + (by + vx * half) + " " +
                (bx + vy * half) + "," + (by - vx * half));
        }
    });
};
