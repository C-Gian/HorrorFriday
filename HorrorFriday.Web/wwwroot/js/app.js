let _menuCloseHandler = null;

window.userMenu = {
    open: function (dotNetRef) {
        this.close();
        // setTimeout avoids closing immediately from the same click that opened the menu
        setTimeout(function () {
            _menuCloseHandler = function (e) {
                var menu = document.getElementById('user-menu');
                if (menu && !menu.contains(e.target)) {
                    document.removeEventListener('click', _menuCloseHandler, true);
                    _menuCloseHandler = null;
                    dotNetRef.invokeMethodAsync('CloseMenu');
                }
            };
            document.addEventListener('click', _menuCloseHandler, true);
        }, 0);
    },
    close: function () {
        if (_menuCloseHandler) {
            document.removeEventListener('click', _menuCloseHandler, true);
            _menuCloseHandler = null;
        }
    }
};
