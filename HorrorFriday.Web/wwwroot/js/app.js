// ── Filter state persistence (session) ──────────────────────────────────
window.hfSaveState  = (k, v) => sessionStorage.setItem(k, v);
window.hfLoadState  = (k)    => sessionStorage.getItem(k);
window.hfClearState = (k)    => sessionStorage.removeItem(k);
window.hfScrollY    = ()     => window.scrollY;
window.hfScrollTo   = (y)    => window.scrollTo({ top: Number(y) || 0, behavior: 'auto' });
window.hfRestoreScroll = (y) => {
    const target = Math.max(0, Number(y) || 0);
    const apply = () => window.scrollTo({ top: target, behavior: 'auto' });

    requestAnimationFrame(() => {
        apply();
        setTimeout(apply, 50);
        setTimeout(apply, 150);
        setTimeout(apply, 300);
        setTimeout(apply, 600);
    });
};

let _hfInfiniteObserver = null;
let _hfInfinitePending = false;

window.hfInfiniteScroll = {
    observe: function (element, dotNetRef) {
        if (!element) return;

        this.disconnect();
        _hfInfiniteObserver = new IntersectionObserver(function (entries) {
            if (_hfInfinitePending || !entries.some(function (entry) { return entry.isIntersecting; })) {
                return;
            }

            _hfInfinitePending = true;
            dotNetRef.invokeMethodAsync('LoadMoreResultsAsync')
                .finally(function () { _hfInfinitePending = false; });
        }, { root: null, rootMargin: '700px 0px', threshold: 0.01 });

        _hfInfiniteObserver.observe(element);
    },
    disconnect: function () {
        if (_hfInfiniteObserver) {
            _hfInfiniteObserver.disconnect();
            _hfInfiniteObserver = null;
        }
        _hfInfinitePending = false;
    }
};

window.getBrowserRegion = function () {
    var lang = (navigator.languages && navigator.languages[0]) || navigator.language || '';
    var parts = lang.split('-');
    return parts.length > 1 ? parts[parts.length - 1].toUpperCase() : '';
};

window.focusElement = function (el) {
    if (el) el.focus();
};

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
