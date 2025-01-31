# SpawnDev.BlazorJS.IndexRouting

[![NuGet](https://img.shields.io/nuget/dt/SpawnDev.BlazorJS.IndexRouting.svg?label=SpawnDev.BlazorJS.IndexRouting)](https://www.nuget.org/packages/SpawnDev.BlazorJS.IndexRouting) 

Contains IndexRouter, an alternative Route component for Blazor WebAssembly that routes all pages to `index.html` with the page route stored as a query parameter. This allows hosting methods that do not provide error page redirection, which can cause issues with client-side routing.

Default `<Router>` rendered url:  
`https://www.mysite.com/user/42/profile?style=dark`

`<IndexRouter>` rendered url:  
`https://www.mysite.com/index.html?$=user/42/profile&style=dark`

IComponents can use the `@page` attribute and parameters normally. 

### Demo
[Simple Demo](https://lostbeard.github.io/SpawnDev.BlazorJS.IndexRouting/)

### Getting started
Add the Nuget package `SpawnDev.BlazorJS.IndexRouting` to your project using your package manager of choice.

#### Use IndexRouter
Replace `<Router>` with `<IndexRouter>` in `App.razor`
```cs
@using SpawnDev.BlazorJS.IndexRouting.Routing

<IndexRouter AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <p role="alert">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</IndexRouter>
```

#### Update NavLinks
Change `<NavLink>` `href` values to use `index.html?$=[PAGE_ROUTE]` format in `NavMenu.razor`
```cs
<div class="@NavMenuCssClass nav-scrollable" @onclick="ToggleNavMenu">
    <nav class="flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="index.html" Match="NavLinkMatch.All">
                <span class="bi bi-house-door-fill-nav-menu" aria-hidden="true"></span> Home
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="index.html?$=counter">
                <span class="bi bi-plus-square-fill-nav-menu" aria-hidden="true"></span> Counter
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="index.html?$=weather">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Weather
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="index.html?$=user/42/profile&style=dark">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Query Param Route
            </NavLink>
        </div>
    </nav>
</div>
```

While `NavLinks` will still work without updating the `href` values, the 'active link' indicator they provide will not work correctly.

