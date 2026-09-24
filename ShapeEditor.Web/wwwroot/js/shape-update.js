window.shapeUpdate = (() => {
    let registration;
    let prepareUpdate;
    let applying = false;

    function showPrompt() {
        document.getElementById("update-prompt")?.removeAttribute("hidden");
    }

    function checkWaiting() {
        if (navigator.serviceWorker.controller && registration?.waiting) {
            showPrompt();
        }
    }

    async function checkForUpdate() {
        try {
            await registration?.update();
            checkWaiting();
        } catch (error) {
            console.warn("检查更新失败", error);
        }
    }

    return {
        async init(dotNetObject) {
            if (!("serviceWorker" in navigator)) return;

            prepareUpdate = dotNetObject;
            registration = await navigator.serviceWorker.ready;
            checkWaiting();

            registration.addEventListener("updatefound", () => {
                const worker = registration.installing;
                worker?.addEventListener("statechange", checkWaiting);
            });

            document.addEventListener("visibilitychange", () => {
                if (!document.hidden) void checkForUpdate();
            });

            window.setInterval(checkForUpdate, 30 * 60 * 1000);
            await checkForUpdate();
        },

        async apply() {
            if (applying || !registration?.waiting) return;

            applying = true;
            try {
                await prepareUpdate.invokeMethodAsync("PrepareForUpdate");
                registration.waiting.postMessage({ type: "SKIP_WAITING" });
            } catch (error) {
                applying = false;
                console.error("更新前保存失败", error);
                alert("当前图形未能保存，暂不刷新。请先导出短代码或存档。");
            }
        }
    };
})();

navigator.serviceWorker?.addEventListener("controllerchange", () => {
    if (document.getElementById("update-prompt")?.hasAttribute("hidden") === false) {
        window.location.reload();
    }
});