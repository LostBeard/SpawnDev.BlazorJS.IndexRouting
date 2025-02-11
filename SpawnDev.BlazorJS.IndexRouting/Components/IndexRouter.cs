﻿#nullable disable warnings

using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;
using System.Web;

namespace SpawnDev.BlazorJS.IndexRouting.Routing
{
    /// <summary>
    /// Routes all pages to index.html?$={route} and then routes to the appropriate component
    /// </summary>
    public partial class IndexRouter : IComponent, IHandleAfterRender, IDisposable
    {

        private static string RouteQueryParameterName { get; set; } = "$";

        private static string IndexHtmlFile { get; set; } = "index.html";

        static readonly char[] _queryOrHashStartChar = new[] { '?', '#' };
        // Dictionary is intentionally used instead of ReadOnlyDictionary to reduce Blazor size
        static readonly IReadOnlyDictionary<string, object> _emptyParametersDictionary
            = new Dictionary<string, object>();

        RenderHandle _renderHandle;
        string _baseUri;
        string _locationAbsolute;
        bool _navigationInterceptionEnabled;
        ILogger<IndexRouter> _logger;

        private CancellationTokenSource _onNavigateCts;

        private Task _previousOnNavigateTask = Task.CompletedTask;

        private RouteKey _routeTableLastBuiltForRouteKey;

        private bool _onNavigateCalled;

        [Inject] private NavigationManager NavigationManager { get; set; }

        [Inject] private INavigationInterception NavigationInterception { get; set; }

        [Inject] private ILoggerFactory LoggerFactory { get; set; }

        /// <summary>
        /// Gets or sets the assembly that should be searched for components matching the URI.
        /// </summary>
        [Parameter]
        [EditorRequired]
        public Assembly AppAssembly { get; set; }

        /// <summary>
        /// Gets or sets a collection of additional assemblies that should be searched for components
        /// that can match URIs.
        /// </summary>
        [Parameter] public IEnumerable<Assembly> AdditionalAssemblies { get; set; }

        /// <summary>
        /// Gets or sets the content to display when no match is found for the requested route.
        /// </summary>
        [Parameter]
        [EditorRequired]
        public RenderFragment NotFound { get; set; }

        /// <summary>
        /// Gets or sets the content to display when a match is found for the requested route.
        /// </summary>
        [Parameter]
        [EditorRequired]
        public RenderFragment<RouteData> Found { get; set; }

        /// <summary>
        /// Get or sets the content to display when asynchronous navigation is in progress.
        /// </summary>
        [Parameter] public RenderFragment? Navigating { get; set; }

        /// <summary>
        /// Gets or sets a handler that should be called before navigating to a new page.
        /// </summary>
        [Parameter] public EventCallback<NavigationContext> OnNavigateAsync { get; set; }

        /// <summary>
        /// Gets or sets a flag to indicate whether route matching should prefer exact matches
        /// over wildcards.
        /// <para>This property is obsolete and configuring it does nothing.</para>
        /// </summary>
        [Parameter] public bool PreferExactMatches { get; set; }

        private RouteTable Routes { get; set; }

        /// <inheritdoc />
        public void Attach(RenderHandle renderHandle)
        {
            _logger = LoggerFactory.CreateLogger<IndexRouter>();
            _renderHandle = renderHandle;
            _baseUri = NavigationManager.BaseUri;
            _locationAbsolute = NavigationManager.Uri;
            NavigationManager.LocationChanged += OnLocationChanged;

            VerifyIndexPage();
        }

        /// <inheritdoc />
        public async Task SetParametersAsync(ParameterView parameters)
        {
            parameters.SetParameterProperties(this);

            if (AppAssembly == null)
            {
                throw new InvalidOperationException($"The {nameof(Router)} component requires a value for the parameter {nameof(AppAssembly)}.");
            }

            // Found content is mandatory, because even though we could use something like <RouteView ...> as a
            // reasonable default, if it's not declared explicitly in the template then people will have no way
            // to discover how to customize this (e.g., to add authorization).
            if (Found == null)
            {
                throw new InvalidOperationException($"The {nameof(Router)} component requires a value for the parameter {nameof(Found)}.");
            }

            // NotFound content is mandatory, because even though we could display a default message like "Not found",
            // it has to be specified explicitly so that it can also be wrapped in a specific layout
            if (NotFound == null)
            {
                throw new InvalidOperationException($"The {nameof(Router)} component requires a value for the parameter {nameof(NotFound)}.");
            }

            if (!_onNavigateCalled)
            {
                _onNavigateCalled = true;
                await RunOnNavigateAsync(NavigationManager.ToBaseRelativePath(_locationAbsolute), isNavigationIntercepted: false);
            }

            Refresh(isNavigationIntercepted: false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            NavigationManager.LocationChanged -= OnLocationChanged;
        }

        private static string StringUntilAny(string str, char[] chars)
        {
            var firstIndex = str.IndexOfAny(chars);
            return firstIndex < 0
                ? str
                : str.Substring(0, firstIndex);
        }

        private void RefreshRouteTable()
        {
            var routeKey = new RouteKey(AppAssembly, AdditionalAssemblies);

            if (!routeKey.Equals(_routeTableLastBuiltForRouteKey))
            {
                _routeTableLastBuiltForRouteKey = routeKey;
                Routes = RouteTableFactory.Create(routeKey);
            }
        }

        /// <summary>
        /// Typically called when hot reload is triggered to clear the route caches so changes are picked up.
        /// </summary>
        private void ClearRouteCaches()
        {
            RouteTableFactory.ClearCaches();
            _routeTableLastBuiltForRouteKey = default;
        }

        internal virtual void Refresh(bool isNavigationIntercepted)
        {
            // If an `OnNavigateAsync` task is currently in progress, then wait
            // for it to complete before rendering. Note: because _previousOnNavigateTask
            // is initialized to a CompletedTask on initialization, this will still
            // allow first-render to complete successfully.
            if (_previousOnNavigateTask.Status != TaskStatus.RanToCompletion)
            {
                if (Navigating != null)
                {
                    _renderHandle.Render(Navigating);
                }
                return;
            }

            RefreshRouteTable();

            // 
            var locationAbsoluteUri = new Uri(_locationAbsolute);
            var relativeAddress = new Uri(NavigationManager.BaseUri).MakeRelativeUri(locationAbsoluteUri);

            var locationPath = NavigationManager.ToBaseRelativePath(_locationAbsolute);
            locationPath = StringUntilAny(locationPath, _queryOrHashStartChar);
            // if the path is index.html we replace it with the route query parameter value
            if (locationPath == IndexHtmlFile)
            {
                locationPath = HttpUtility.ParseQueryString(locationAbsoluteUri.Query).Get(RouteQueryParameterName) ?? "";
            }
            var context = new RouteContext(locationPath);
            Routes.Route(context);

            if (context.Handler != null)
            {
                if (!typeof(IComponent).IsAssignableFrom(context.Handler))
                {
                    throw new InvalidOperationException($"The type {context.Handler.FullName} " +
                        $"does not implement {typeof(IComponent).FullName}.");
                }

                Log.NavigatingToComponent(_logger, context.Handler, locationPath, _baseUri);

                var routeData = new RouteData(
                    context.Handler,
                    context.Parameters ?? _emptyParametersDictionary);
                _renderHandle.Render(Found(routeData));
            }
            else
            {
                if (!isNavigationIntercepted)
                {
                    Log.DisplayingNotFound(_logger, locationPath, _baseUri);

                    // We did not find a Component that matches the route.
                    // Only show the NotFound content if the application developer programatically got us here i.e we did not
                    // intercept the navigation. In all other cases, force a browser navigation since this could be non-Blazor content.
                    _renderHandle.Render(NotFound);
                }
                else
                {
                    Log.NavigatingToExternalUri(_logger, _locationAbsolute, locationPath, _baseUri);
                    NavigationManager.NavigateTo(_locationAbsolute, forceLoad: true);
                }
            }
        }

        internal async ValueTask RunOnNavigateAsync(string path, bool isNavigationIntercepted)
        {
            // Cancel the CTS instead of disposing it, since disposing does not
            // actually cancel and can cause unintended Object Disposed Exceptions.
            // This effectivelly cancels the previously running task and completes it.
            _onNavigateCts?.Cancel();
            // Then make sure that the task has been completely cancelled or completed
            // before starting the next one. This avoid race conditions where the cancellation
            // for the previous task was set but not fully completed by the time we get to this
            // invocation.
            await _previousOnNavigateTask;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _previousOnNavigateTask = tcs.Task;

            if (!OnNavigateAsync.HasDelegate)
            {
                Refresh(isNavigationIntercepted);
            }

            _onNavigateCts = new CancellationTokenSource();
            var navigateContext = new NavigationContext(path, _onNavigateCts.Token);

            var cancellationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            navigateContext.CancellationToken.Register(state =>
                ((TaskCompletionSource)state).SetResult(), cancellationTcs);

            try
            {
                // Task.WhenAny returns a Task<Task> so we need to await twice to unwrap the exception
                var task = await Task.WhenAny(OnNavigateAsync.InvokeAsync(navigateContext), cancellationTcs.Task);
                await task;
                tcs.SetResult();
                Refresh(isNavigationIntercepted);
            }
            catch (Exception e)
            {
                _renderHandle.Render(builder => ExceptionDispatchInfo.Throw(e));
            }
        }
        /// <summary>
        /// If true, navigation to any page except index.html will be redirected to index.html with the route as a query parameter
        /// </summary>
        [Parameter]
        public bool IndexLock { get; set; } = true;
        bool VerifyIndexPage()
        {
            if (!IndexLock)
            {
                return true;
            }
            var indexUrl = MakeIndexUrl(NavigationManager);
            if (indexUrl != null)
            {
                NavigationManager.NavigateTo(indexUrl, false, true);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Creates a url for the specified location that will render using index.html as the path and the route appended as a query parameter
        /// </summary>
        public static string? MakeIndexUrl(NavigationManager navigationManager)
        {
            return MakeIndexUrl(navigationManager, navigationManager.Uri);
        }
        /// <summary>
        /// Creates a url for the specified location that will render using index.html as the path and the route appended as a query parameter
        /// </summary>
        /// <param name="navigationManager"></param>
        /// <param name="location"></param>
        /// <returns></returns>
        public static string? MakeIndexUrl(NavigationManager navigationManager, string location)
        {
            var locationPathSub = navigationManager.ToBaseRelativePath(location);
            var locationPath = StringUntilAny(locationPathSub, _queryOrHashStartChar);
            if (locationPath != IndexHtmlFile)
            {
                var locationUri = new Uri(location);
                var qs = HttpUtility.ParseQueryString(locationUri.Query);
                if (string.IsNullOrEmpty(locationPath))
                {
                    qs.Remove(RouteQueryParameterName);
                }
                else
                {
                    qs.Set(RouteQueryParameterName, locationPath);
                }
                var newQuery = qs.ToString();
                var newPath = $"{IndexHtmlFile}{(string.IsNullOrEmpty(newQuery) ? "" : $"?{newQuery}")}";
                return newPath;
            }
            return null;
        }
        private void OnLocationChanged(object sender, LocationChangedEventArgs args)
        {
            if (!VerifyIndexPage())
            {
                // page will change
                return;
            }
            _locationAbsolute = args.Location;
            if (_renderHandle.IsInitialized && Routes != null)
            {
                _ = RunOnNavigateAsync(NavigationManager.ToBaseRelativePath(_locationAbsolute), args.IsNavigationIntercepted).Preserve();
            }
        }

        Task IHandleAfterRender.OnAfterRenderAsync()
        {
            if (!_navigationInterceptionEnabled)
            {
                _navigationInterceptionEnabled = true;
                return NavigationInterception.EnableNavigationInterceptionAsync();
            }

            return Task.CompletedTask;
        }

        private static partial class Log
        {
            [LoggerMessage(1, LogLevel.Debug, $"Displaying {nameof(NotFound)} because path '{{Path}}' with base URI '{{BaseUri}}' does not match any component route", EventName = "DisplayingNotFound")]
            internal static partial void DisplayingNotFound(ILogger logger, string path, string baseUri);

            [LoggerMessage(2, LogLevel.Debug, "Navigating to component {ComponentType} in response to path '{Path}' with base URI '{BaseUri}'", EventName = "NavigatingToComponent")]
            internal static partial void NavigatingToComponent(ILogger logger, Type componentType, string path, string baseUri);

            [LoggerMessage(3, LogLevel.Debug, "Navigating to non-component URI '{ExternalUri}' in response to path '{Path}' with base URI '{BaseUri}'", EventName = "NavigatingToExternalUri")]
            internal static partial void NavigatingToExternalUri(ILogger logger, string externalUri, string path, string baseUri);
        }
    }
}