define(["emby-input", "emby-button", "emby-checkbox", "emby-select"], function () {
    return function (view) {
        var pluginUniqueId = '7f3a9c2e-4b1d-4e8f-9a6c-2d5b8e1f3a07';

        function load(page) {
            Dashboard.showLoadingMsg();
            ApiClient.getPluginConfiguration(pluginUniqueId).then(function (config) {
                page.querySelector('#UnwatchedDaysThreshold').value = config.UnwatchedDaysThreshold;
                page.querySelector('#GracePeriodDays').value = config.GracePeriodDays;
                page.querySelector('#MinimumLibraryAgeDays').value = config.MinimumLibraryAgeDays;
                page.querySelector('#Mode').value = config.Mode;
                page.querySelector('#DryRun').checked = config.DryRun;
                page.querySelector('#DeleteFiles').checked = config.DeleteFiles;
                page.querySelector('#EnableMovies').checked = config.EnableMovies;
                page.querySelector('#EnableSeries').checked = config.EnableSeries;
                page.querySelector('#ExcludeFavorites').checked = config.ExcludeFavorites;
                page.querySelector('#ExcludedTags').value = config.ExcludedTags;
                page.querySelector('#CollectionName').value = config.CollectionName;
                page.querySelector('#RadarrUrl').value = config.RadarrUrl;
                page.querySelector('#RadarrApiKey').value = config.RadarrApiKey;
                page.querySelector('#SonarrUrl').value = config.SonarrUrl;
                page.querySelector('#SonarrApiKey').value = config.SonarrApiKey;
                Dashboard.hideLoadingMsg();
            });
        }

        view.querySelector('#LeavingSoonConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();
            ApiClient.getPluginConfiguration(pluginUniqueId).then(function (config) {
                config.UnwatchedDaysThreshold = parseInt(view.querySelector('#UnwatchedDaysThreshold').value, 10);
                config.GracePeriodDays = parseInt(view.querySelector('#GracePeriodDays').value, 10);
                config.MinimumLibraryAgeDays = parseInt(view.querySelector('#MinimumLibraryAgeDays').value, 10);
                config.Mode = parseInt(view.querySelector('#Mode').value, 10);
                config.DryRun = view.querySelector('#DryRun').checked;
                config.DeleteFiles = view.querySelector('#DeleteFiles').checked;
                config.EnableMovies = view.querySelector('#EnableMovies').checked;
                config.EnableSeries = view.querySelector('#EnableSeries').checked;
                config.ExcludeFavorites = view.querySelector('#ExcludeFavorites').checked;
                config.ExcludedTags = view.querySelector('#ExcludedTags').value;
                config.CollectionName = view.querySelector('#CollectionName').value;
                config.RadarrUrl = view.querySelector('#RadarrUrl').value;
                config.RadarrApiKey = view.querySelector('#RadarrApiKey').value;
                config.SonarrUrl = view.querySelector('#SonarrUrl').value;
                config.SonarrApiKey = view.querySelector('#SonarrApiKey').value;
                ApiClient.updatePluginConfiguration(pluginUniqueId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            });
            return false;
        });

        view.addEventListener('pageshow', function () {
            load(view);
        });
    };
});
