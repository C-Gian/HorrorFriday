// Pre-load client ID from appsettings.json so it's ready before Blazor initializes
(async function () {
    try {
        const resp = await fetch('/appsettings.json');
        const cfg = await resp.json();
        window.__googleClientId = cfg?.Google?.ClientId ?? '';
    } catch {
        window.__googleClientId = '';
    }
})();

window.googleAuth = {
    initAndRender: function (elementId, dotNetRef) {
        window.__googleDotNetRef = dotNetRef;
        // setTimeout escapes Blazor's interop call stack, which fixes the
        // "Illegal invocation" error on navigator.permissions.query inside GSI
        setTimeout(function () {
            if (typeof google === 'undefined' || !window.__googleClientId) return;

            google.accounts.id.initialize({
                client_id: window.__googleClientId,
                callback: function (response) {
                    window.__googleDotNetRef.invokeMethodAsync('OnGoogleCredential', response.credential);
                },
                auto_select: true,
                itp_support: true,
                context: 'signin'
            });

            const el = document.getElementById(elementId);
            if (el) {
                google.accounts.id.renderButton(el, {
                    theme: 'filled_black',
                    size: 'large',
                    width: 300,
                    text: 'continue_with',
                    shape: 'rectangular'
                });
            }

            // Shows One Tap if the user is already signed in to Google in the browser
            google.accounts.id.prompt();
        }, 0);
    }
};
