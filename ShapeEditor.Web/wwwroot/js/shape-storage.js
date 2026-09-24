window.shapeStorage = {
    save: function (key, value) {
        localStorage.setItem(key, value);
    },

    load: function (key) {
        return localStorage.getItem(key);
    },

    remove: function (key) {
        localStorage.removeItem(key);
    },

    downloadText: function (fileName, content, contentType) {
        const blob = new Blob(
            [content],
            { type: contentType });

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
    }
};