// 顶栏下载菜单：点击外部或按 Esc 时收起。
document.addEventListener("click", function (event) {
    document
        .querySelectorAll("details.download-menu[open]")
        .forEach(function (menu) {
            if (!menu.contains(event.target)) {
                menu.removeAttribute("open");
            }
        });
});

document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape") {
        return;
    }

    document
        .querySelectorAll("details.download-menu[open]")
        .forEach(function (menu) {
            menu.removeAttribute("open");
            menu.querySelector("summary")?.focus();
        });
});
