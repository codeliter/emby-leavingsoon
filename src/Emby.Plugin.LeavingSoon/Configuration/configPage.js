export default function (view) {
const pluginId = '7f3a9c2e-4b1d-4e8f-9a6c-2d5b8e1f3a07';

function load(page) {
    Dashboard.showLoadingMsg();
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
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
    const page = view;
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
        config.UnwatchedDaysThreshold = parseInt(page.querySelector('#UnwatchedDaysThreshold').value, 10);
        config.GracePeriodDays = parseInt(page.querySelector('#GracePeriodDays').value, 10);
        config.MinimumLibraryAgeDays = parseInt(page.querySelector('#MinimumLibraryAgeDays').value, 10);
        config.Mode = parseInt(page.querySelector('#Mode').value, 10);
        config.DryRun = page.querySelector('#DryRun').checked;
        config.DeleteFiles = page.querySelector('#DeleteFiles').checked;
        config.EnableMovies = page.querySelector('#EnableMovies').checked;
        config.EnableSeries = page.querySelector('#EnableSeries').checked;
        config.ExcludeFavorites = page.querySelector('#ExcludeFavorites').checked;
        config.ExcludedTags = page.querySelector('#ExcludedTags').value;
        config.CollectionName = page.querySelector('#CollectionName').value;
        config.RadarrUrl = page.querySelector('#RadarrUrl').value;
        config.RadarrApiKey = page.querySelector('#RadarrApiKey').value;
        config.SonarrUrl = page.querySelector('#SonarrUrl').value;
        config.SonarrApiKey = page.querySelector('#SonarrApiKey').value;
        ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
        });
    });
    return false;
});

view.addEventListener('pageshow', function () {
    load(view);
});
