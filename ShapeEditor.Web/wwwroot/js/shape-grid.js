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

        const baseCellWidth = 100;
        const baseCellHeight = 76;
        const margin = 12;

        const fitScale = Math.min(
            (viewWidth - margin * 2) / (columns * baseCellWidth),
            (viewHeight - margin * 2) / (rows * baseCellHeight),
            1.45
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
                    cellHeight
                );
            }
        }

        drawGrid(ctx, rows, columns, cellWidth, cellHeight);

        if (state.selectedLayer !== null && state.selectedPart !== null) {
            drawSelectedCell(
                ctx,
                state,
                state.selectedLayer,
                state.selectedPart,
                cellWidth,
                cellHeight
            );
        }

        ctx.restore();
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
    const cellWidth = 128;
    const cellHeight = 96;

    const width =
        state.columns * cellWidth;

    const height =
        state.rows * cellHeight;

    const maxImageSide = 16000;

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
                cellHeight);
        }
    }

    drawGrid(
        ctx,
        state.rows,
        state.columns,
        cellWidth,
        cellHeight);

    return canvas;
}

function drawCell(ctx, cell, x, y, width, height) {
    if (cell.type === "empty") {
        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(x, y, width, height);
        return;
    }

    if (cell.type === "pin") {
        ctx.fillStyle = "#FFFFFF";
        ctx.fillRect(x, y, width, height);

        ctx.fillStyle = "#000000";
        ctx.fillRect(
            x + width / 3,
            y,
            width / 3,
            height);

        return;
    }

    const color =
        getColor(cell.colorCode);

    if (cell.type === "crystal") {
        drawCrystalBands(
            ctx,
            color,
            x,
            y,
            width,
            height);

        drawLightBorder(
            ctx,
            x,
            y,
            width,
            height);
    }
    else {
        ctx.fillStyle = color;
        ctx.fillRect(x, y, width, height);

        drawLightBorder(
            ctx,
            x,
            y,
            width,
            height);
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
    baseColor,
    x,
    y,
    width,
    height) {
    const colors =
        createCrystalBands(baseColor);

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
    height) {
    ctx.strokeStyle =
        "rgba(255,255,255,0.72)";

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
    cellHeight) {
    const width =
        columns * cellWidth;

    const height =
        rows * cellHeight;

    ctx.save();

    ctx.strokeStyle = "#9E9E9E";
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

function getColor(code) {
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
    cellHeight) {
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

    ctx.strokeStyle = "#1976D2";
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