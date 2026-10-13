(function () {
    function updateBodyState() {
        const hasFallbackDialog = Boolean(document.querySelector("dialog.is-modal-fallback[open]"));
        document.body.classList.toggle("has-modal-fallback", hasFallbackDialog);
    }

    document.querySelectorAll("dialog").forEach((dialog) => {
        if (typeof dialog.showModal === "function") {
            return;
        }

        dialog.showModal = function () {
            if (dialog.hasAttribute("open")) return;

            dialog.setAttribute("open", "");
            dialog.setAttribute("aria-modal", "true");
            dialog.classList.add("is-modal-fallback");
            updateBodyState();
        };

        dialog.close = function (returnValue = "") {
            if (!dialog.hasAttribute("open")) return;

            dialog.returnValue = returnValue;
            dialog.removeAttribute("open");
            dialog.removeAttribute("aria-modal");
            dialog.classList.remove("is-modal-fallback");
            updateBodyState();
            dialog.dispatchEvent(new Event("close"));
        };

        dialog.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                event.preventDefault();
                dialog.close();
            }
        });
    });
})();
