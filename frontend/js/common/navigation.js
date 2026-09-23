(function () {
    const navItems = document.querySelectorAll(".nav-item");

    function setActiveNavigation(activeItem) {
        navItems.forEach((navItem) => navItem.classList.remove("is-active"));
        activeItem.classList.add("is-active");
    }

    function showScreen(screenName) {
        const targetScreen = document.querySelector(`#${screenName}-screen`);

        if (!targetScreen) {
            return;
        }

        document.querySelectorAll(".screen").forEach((screen) => {
            screen.classList.remove("is-visible");
        });

        targetScreen.classList.add("is-visible");
    }

    navItems.forEach((item) => {
        item.addEventListener("click", () => {
            setActiveNavigation(item);
            showScreen(item.dataset.screen);
        });
    });
})();
