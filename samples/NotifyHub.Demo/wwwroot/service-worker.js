self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { data = { title: 'NotifyHub', body: event.data ? event.data.text() : '' }; }

    const title = data.title || 'NotifyHub';
    const options = {
        body: data.body || '',
        tag: 'notifyhub-demo',
        renotify: true,
        data: data.url ? { url: data.url } : {},
    };

    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = event.notification.data && event.notification.data.url;
    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clients => {
            if (clients.length > 0) return clients[0].focus();
            return self.clients.openWindow(url || '/');
        })
    );
});
