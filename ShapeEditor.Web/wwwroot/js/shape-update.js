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

    /*
     * Service Worker 只负责更新提示和离线缓存。
     * 这里不能等待 navigator.serviceWorker.ready：
     * 一旦注册失效（例如安装阶段的完整性校验不通过），
     * ready 永远不会 resolve，等待它的调用方会一直挂起。
     */
    async function watchForUpdates() {
        try {
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
        } catch (error) {
            console.warn("Service Worker 更新检查未启动", error);
        }
    }

    return {
        init(dotNetObject) {
            if (!("serviceWorker" in navigator)) return;

            prepareUpdate = dotNetObject;

            void watchForUpdates();
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
