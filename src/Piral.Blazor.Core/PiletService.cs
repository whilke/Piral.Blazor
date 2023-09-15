﻿using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using Piral.Blazor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Piral.Blazor.Core;

public sealed class PiletService : IPiletService
{
    private readonly Uri _baseUrl;
    private readonly IConfiguration _config;
    private readonly string _name;
    private readonly string _version;
    private readonly IJSRuntime _js;
    private readonly HttpClient _client;
    private readonly bool _core;
    private readonly List<EventListener> _listeners;
    private readonly List<string> _loadedLanguages;
    private readonly Dictionary<string, List<string>> _satellites;

    public event EventHandler LanguageChanged;

    private PiletService(IJSRuntime js, HttpClient client, bool core)
    {
        _js = js;
        _listeners = new List<EventListener>();
        _client = client;
        _core = core;
        _loadedLanguages = new List<string>();
    }

    public PiletService(IJSRuntime js, HttpClient client, string baseUrl) : this(js, client, false)
    {
        _name = "(unknown)";
        _version = "0.0.0";

        // Move base URL up in case of a `_framework` containment
        _baseUrl = new Uri(baseUrl.Replace("/_framework/", "/"));
        // Create config wrapper
        _config = new ConfigurationBuilder().AddJsonStream(GetStream("{}")).Build();
    }

    public PiletService(IJSRuntime js, HttpClient client, bool core, PiletDefinition pilet) : this(js, client, core)
    {
        _name = pilet.Name;
        _version = pilet.Version;

        // define satellites
        _satellites = pilet.Satellites;
        // base URL is already given
        _baseUrl = new Uri(pilet.BaseUrl);
        // Create config wrapper
        _config = new ConfigurationBuilder().AddJsonStream(GetStream(pilet.Config)).Build();

        LoadLanguage(Localization.Language).Forget();
    }

    public string Name => _name;

    public string Version => _version;

    public bool IsCore => _core;

    public IConfiguration Config => _config;

    public string GetUrl(string localPath)
    {
        if (localPath.StartsWith("/") && !localPath.StartsWith("//"))
        {
            localPath = localPath.Substring(1);
        }

        return new Uri(_baseUrl, localPath).AbsoluteUri;
    }

    public async Task LoadLanguage(string language)
    {
        if (!_loadedLanguages.Contains(language))
        {
            _loadedLanguages.Add(language);

            if (_satellites?.TryGetValue(language, out var satellites) ?? false)
            {
                foreach (var satellite in satellites)
                {
                    var url = GetUrl(satellite);
                    var dep = await _client.GetStreamAsync(url);
                    AssemblyLoadContext.Default.LoadFromStream(dep);
                }
            }

            // we also support loading region specific languages, e.g., "de-AT"
            // in this case we also need to take the "front" to load all satellites
            var idx = language.IndexOf('-');

            if (idx != -1)
            {
                var primaryLanguage = language.Substring(0, idx);
                await LoadLanguage(primaryLanguage);
            }
        }
    }

    public void InformLanguageChange() => LanguageChanged?.Invoke(this, EventArgs.Empty);

    public void DispatchEvent<T>(string type, T args)
    {
        _js.InvokeVoidAsync("Blazor.emitPiralEvent", type, args);
    }

    public void AddEventListener<T>(string type, Action<T> handler)
    {
        _listeners.Add(
            new EventListener
            {
                Handler = (json) =>
                {
                    handler.Invoke(json.Deserialize<T>());
                },
                Reference = handler,
                Type = type,
            }
        );
    }

    public void RemoveEventListener<T>(string type, Action<T> handler)
    {
        _listeners.RemoveAll(listener => listener.Type == type && Object.ReferenceEquals(listener.Reference, handler));
    }

    public void CallEventListeners(string type, JsonElement args)
    {
        foreach (var listener in _listeners)
        {
            if (listener.Type == type)
            {
                listener.Handler.Invoke(args);
            }
        }
    }

    private static Stream GetStream(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    struct EventListener
    {
        public object Reference;
        public string Type;
        public Action<JsonElement> Handler;
    }
}
