define(["emby-button"], function () {
    return function (view) {
        var statusEl = view.querySelector('#LeavingSoonStatus');
        var listEl = view.querySelector('#LeavingSoonList');
        var auditEl = view.querySelector('#LeavingSoonAudit');

        function daysInCollection(isoDate) {
            var added = new Date(isoDate);
            return Math.floor((Date.now() - added.getTime()) / 86400000);
        }

        function renderItems(items) {
            listEl.innerHTML = '';
            if (!items.length) {
                statusEl.textContent = 'Nothing is leaving soon. Your library is all caught up.';
                return;
            }

            statusEl.textContent = items.length + (items.length === 1 ? ' item' : ' items') + ' in the collection:';

            items.forEach(function (item) {
                var row = document.createElement('div');
                row.style.display = 'flex';
                row.style.alignItems = 'center';
                row.style.gap = '1em';
                row.style.padding = '0.75em 1em';
                row.style.borderRadius = '0.5em';
                row.style.background = 'rgba(128,128,128,0.08)';

                var label = document.createElement('div');
                label.style.flex = '1';
                var title = document.createElement('div');
                title.textContent = item.Name;
                var sub = document.createElement('div');
                sub.className = 'fieldDescription';
                sub.textContent = item.MediaType + ' · in collection for ' + daysInCollection(item.AddedToCollectionUtc) + ' days' + (item.Approved ? ' · removal approved' : '');
                label.appendChild(title);
                label.appendChild(sub);
                row.appendChild(label);

                var approveBtn = document.createElement('button');
                approveBtn.setAttribute('is', 'emby-button');
                approveBtn.type = 'button';
                approveBtn.className = 'raised emby-button';
                approveBtn.textContent = item.Approved ? 'Approved' : 'Approve removal';
                approveBtn.disabled = item.Approved;
                approveBtn.addEventListener('click', function () {
                    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('LeavingSoon/Approve/' + item.ItemId) }).then(load);
                });
                row.appendChild(approveBtn);

                var rescueBtn = document.createElement('button');
                rescueBtn.setAttribute('is', 'emby-button');
                rescueBtn.type = 'button';
                rescueBtn.className = 'raised emby-button';
                rescueBtn.textContent = 'Keep';
                rescueBtn.addEventListener('click', function () {
                    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('LeavingSoon/Rescue/' + item.ItemId) }).then(load);
                });
                row.appendChild(rescueBtn);

                listEl.appendChild(row);
            });
        }

        function renderAudit(entries) {
            if (!entries.length) {
                auditEl.textContent = 'No activity yet.';
                return;
            }

            auditEl.textContent = entries.slice(-15).reverse().map(function (e) {
                return new Date(e.TimestampUtc).toLocaleString() + '  [' + e.Action + ']  ' + e.Detail;
            }).join('\n');
        }

        function load() {
            ApiClient.getJSON(ApiClient.getUrl('LeavingSoon/Candidates')).then(renderItems);
            ApiClient.getJSON(ApiClient.getUrl('LeavingSoon/Audit')).then(renderAudit);
        }

        view.addEventListener('pageshow', load);
    };
});
