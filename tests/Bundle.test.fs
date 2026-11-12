module Tests.BundleTests

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Vitest
open Tests.Fixtures
open Time

[<Import("readFileSync", "node:fs")>]
let private readFile (path: string) (encoding: string) : string = jsNative

type private IWindow =
    abstract document: Browser.Types.Document
    abstract location: Browser.Types.Location
    abstract eval: string -> unit
    abstract close: unit -> unit

type private IDom =
    abstract window: IWindow

[<Import("JSDOM", "jsdom"); Emit("new $0($1, $2)")>]
let private createDom (html: string) (options: obj) : IDom = jsNative

type private ManifestIcon =
    abstract src: string

type private Manifest =
    abstract id: string
    abstract start_url: string
    abstract scope: string
    abstract icons: ManifestIcon array

[<Emit("JSON.parse(atob($0.substring($0.indexOf(',') + 1)))")>]
let private decodeManifest (dataUrl: string) : Manifest = jsNative

[<Emit("$0.Date = Date; $0.setInterval = setInterval; $0.clearInterval = clearInterval")>]
let private useTestClock (window: IWindow) : unit = jsNative

[<Emit("Object.defineProperty($0.navigator, 'geolocation', { configurable: true, value: $1 })")>]
let private setGeolocation (window: IWindow) (geolocation: obj) : unit = jsNative

type private BundlePage(dom: IDom) =
    member _.Advance(duration: int<milliseconds>) =
        vi.advanceTimersByTimeAsync (int duration)

    member _.SetTime(instant: float<milliseconds>) =
        vi.setSystemTime ((instantFromMilliseconds instant).UtcDateTime)

    member _.Hash = dom.window.location.hash

    member _.Query(selector: string) =
        dom.window.document.querySelector selector

    member _.WaitFor(assertion: unit -> unit) =
        RTL.waitFor (assertion, RTLWaitForOptions(container = dom.window.document.body))

    member _.SelectStation(id: string) =
        RTL.fireEvent.change (
            dom.window.document.querySelector "#stop-select" :?> Browser.Types.HTMLElement,
            createObj [ "target" ==> {| value = id |} ]
        )

    member _.Click(selector: string) =
        RTL.fireEvent.click (dom.window.document.querySelector selector :?> Browser.Types.HTMLElement)

    interface IDisposable with
        member _.Dispose() =
            vi.clearAllTimers ()
            dom.window.close ()

let private startBundle (hash: string) (fetch: obj) (geolocation: GeoRequest option) =
    let html = readFile "dist/index.html" "utf8"

    let script =
        Regex.Match(html, "<script[^>]*type=\"module\"[^>]*>([\\s\\S]*?)</script>")

    Vitest.expect(script.Success).toBe true

    let dom =
        createDom
            html
            (createObj
                [ "url" ==> $"https://g.apps.jasonmc.net/{hash}"
                  "runScripts" ==> "outside-only" ])
    // Share Vitest's controlled Date/intervals with the page. Keep timeouts real
    // so bundled async code and RTL's polling can run normally in both realms.
    vi.useFakeTimers (createObj [ "toFake" ==> [| "Date"; "setInterval"; "clearInterval" |] ])
    |> ignore

    vi.setSystemTime ((instantFromMilliseconds fixedNowMs).UtcDateTime)
    useTestClock dom.window

    let geo =
        geolocation
        |> Option.map (fun request ->
            createObj [ "getCurrentPosition" ==> Action<GeoSuccess, GeoError, GeoOptions>(request) ])
        |> Option.defaultValue null

    setGeolocation dom.window geo
    dom.window?fetch <- fetch

    try
        dom.window.eval script.Groups[1].Value
        new BundlePage(dom)
    with _ ->
        vi.clearAllTimers ()
        dom.window.close ()
        reraise ()

Vitest.test (
    "production metadata embeds the manifest and icons with usable launch URLs",
    fun () ->
        let dom = createDom (readFile "dist/index.html" "utf8") (createObj [])

        try
            let document = dom.window.document
            let href = document.querySelector("link[rel='manifest']").getAttribute "href"
            Vitest.expect(href.StartsWith "data:application/manifest+json;base64,").toBe true
            let manifest = decodeManifest href

            let siteUrl =
                document.querySelector("meta[property='og:url']").getAttribute "content"

            Vitest.expect(manifest.id).toBe siteUrl
            Vitest.expect(manifest.start_url).toBe siteUrl
            Vitest.expect(manifest.scope).toBe siteUrl
            Vitest.expect(manifest.icons.Length).toBe 2

            for icon in manifest.icons do
                Vitest.expect(icon.src.StartsWith "data:image/png;base64,").toBe true

            let favicons = document.querySelectorAll ("link[rel='icon']")
            Vitest.expect(favicons.length).toBe 2

            for index in 0 .. favicons.length - 1 do
                let icon = favicons[index]
                Vitest.expect((icon.getAttribute "href").StartsWith "data:image/").toBe true
        finally
            dom.window.close ()
)

Vitest.test (
    "the production bundle groups destinations and selects stations through the real dropdown",
    fun () ->
        promise {
            let buffer =
                encode
                    [| trip
                           "north-1"
                           "north-1"
                           0
                           [| call "G26N" 120.0<seconds>
                              call "G24N" 180.0<seconds>
                              call "G22N" 240.0<seconds> |]
                       trip "short" "short" 0 [| call "G26N" 150.0<seconds>; call "G24N" 210.0<seconds> |]
                       trip
                           "north-2"
                           "north-2"
                           0
                           [| call "G26N" 240.0<seconds>
                              call "G24N" 300.0<seconds>
                              call "G22N" 360.0<seconds> |]
                       trip
                           "south"
                           "south"
                           1
                           [| call "G22S" 30.0<seconds>
                              call "G24S" 60.0<seconds>
                              call "G26S" 120.0<seconds>
                              call "F27S" 1200.0<seconds> |] |]

            let fetch =
                vi.fn (fun (_: string) -> JS.Constructors.Promise.resolve (response buffer 200))

            let mutable geoRequests = 0

            use page =
                startBundle "#G26" fetch (Some(fun _ _ _ -> geoRequests <- geoRequests + 1))

            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#lists .arrival").toBeInTheDocument ())
            let court = page.Query "h2[aria-label='Northbound to Court Sq']"
            let short = page.Query "h2[aria-label='Northbound to 21 St']"
            Vitest.expect(court.textContent).toBe "Court Sq"
            Vitest.expect(court.closest(".card").Value.querySelectorAll(".arrival").length).toBe 2
            Vitest.expect(short.closest(".card").Value.querySelectorAll(".arrival").length).toBe 1
            Vitest.expect(page.Query "h2[aria-label='Southbound to Church Av']").toHaveTextContent "Church Av"
            Vitest.expect(page.Query "#stop-select").toHaveValue "G26"
            Vitest.expect(page.Query "[data-trip-id='north-1'] .meta").toHaveTextContent "in 2 min"
            Vitest.expect(geoRequests).toBe 0

            page.SelectStation "G24"
            do! page.WaitFor(fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)

            do!
                page.WaitFor(fun () ->
                    Vitest.expect(page.Query "[data-trip-id='north-1'] .meta").toHaveTextContent "in 3 min")

            Vitest.expect(page.Query "#stop-select").toHaveValue "G24"
            Vitest.expect(page.Hash).toBe "#G24"
            Vitest.expect(page.Query "#lists .arrival[data-trip-id='short']").toBeNull ()
            Vitest.expect(page.Query "h2[aria-label='Northbound to 21 St']").toBeNull ()

            page.SelectStation "G22"
            do! page.WaitFor(fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 3)

            do!
                page.WaitFor(fun () ->
                    Vitest.expect(page.Query "[data-trip-id='south'] .meta").toHaveTextContent "<1 min")

            Vitest.expect(page.Query "h2[aria-label='Northbound to Court Sq']").toBeNull ()
            Vitest.expect(page.Query("#lists").querySelectorAll(".arrival").length).toBe 1
            Vitest.expect(page.Query "h2[aria-label='Southbound to Church Av']").toHaveTextContent "Church Av"
            Vitest.expect(page.Query "#stop-select").toHaveValue "G22"
        }
)

Vitest.test (
    "the production bundle preserves a manual station choice when geolocation resolves later",
    fun () ->
        promise {
            let buffer = trainsFeed ()

            let fetch =
                vi.fn (fun (_: string) -> JS.Constructors.Promise.resolve (response buffer 200))

            let mutable locate: GeoSuccess option = None
            use page = startBundle "" fetch (Some(fun success _ _ -> locate <- Some success))
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#lists .arrival").toBeInTheDocument ())
            do! page.WaitFor(fun () -> Vitest.expect(locate.IsSome).toBe true)
            page.SelectStation "G24"
            do! page.WaitFor(fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)
            locate.Value(position 40.746554 -73.943832)
            do! page.WaitFor(fun () -> Vitest.expect(page.Query ".osm-user-location").toBeInTheDocument ())
            Vitest.expect(page.Query "#stop-select").toHaveValue "G24"
            Vitest.expect(page.Hash).toBe "#G24"
            Vitest.expect(fetch).toHaveBeenCalledTimes 2
        }
)

Vitest.test (
    "the production bundle updates countdowns and recovers from a failed refresh",
    fun () ->
        promise {
            let buffer = arrivalsFeed ()
            let mutable failNext = false

            let fetch =
                vi.fn (fun (_: string) ->
                    let status = if failNext then 500 else 200
                    failNext <- false
                    JS.Constructors.Promise.resolve (response buffer status))

            use page = startBundle "#G26" fetch None
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#lists .meta").toHaveTextContent "in 2 min")
            page.SetTime(fixedNowMs + 60000.0<milliseconds>)
            do! page.Advance 1000<milliseconds>
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#lists .meta").toHaveTextContent "<1 min")

            let arrivalTimes () =
                let times = page.Query("#lists").querySelectorAll ".time"
                [| for index in 0 .. times.length - 1 -> times[index].textContent |]

            let beforeFailure = arrivalTimes ()
            failNext <- true
            do! page.Advance 4000<milliseconds>
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#status.error").toHaveTextContent "500")
            Vitest.expect(fetch).toHaveBeenCalledTimes 2
            Vitest.expect(arrivalTimes ()).toEqual beforeFailure
            do! page.Advance 1000<milliseconds>
            Vitest.expect(page.Query "#status.error").toHaveTextContent "500"
            do! page.Advance 4000<milliseconds>
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#status").toHaveTextContent "Feed age:")
            Vitest.expect(page.Query("#status").classList.contains "error").toBe false
            Vitest.expect(fetch).toHaveBeenCalledTimes 3
            Vitest.expect(page.Query "#lists .arrival").toBeInTheDocument ()
        }
)

Vitest.test (
    "the production bundle moves map trains, switches views, and selects map stations",
    fun () ->
        promise {
            let buffer = trainsFeed ()

            let fetch =
                vi.fn (fun (_: string) -> JS.Constructors.Promise.resolve (response buffer 200))

            use page = startBundle "#G26" fetch None

            do!
                page.WaitFor(fun () ->
                    Vitest.expect(page.Query ".osm-train[data-trip-id='G-N-1']").toBeInTheDocument ())

            Vitest.expect(page.Query ".osm-track-line").toBeInTheDocument ()
            Vitest.expect(page.Query ".osm-train[data-trip-id='999_G..S']").toBeNull ()

            let latitude () =
                float (page.Query(".osm-train[data-trip-id='G-N-1']").getAttribute "data-lat")

            let before = latitude ()
            do! page.Advance 30000<milliseconds>
            do! page.WaitFor(fun () -> Vitest.expect(latitude ()).toBeGreaterThan before)

            page.Click "button[aria-controls='map']"
            Vitest.expect(page.Query ".arrivals-column").toHaveClass "is-hidden-mobile"
            Vitest.expect(page.Query "button[aria-controls='map']").toHaveAttribute ("aria-pressed", "true")
            page.Click "[data-stop-id='G29']"
            do! page.WaitFor(fun () -> Vitest.expect(page.Query "#stop-select").toHaveValue "G29")
            Vitest.expect(page.Hash).toBe "#G29"
            Vitest.expect(page.Query "[data-stop-id='G29']").toHaveClass "osm-stop-selected"
            page.Click "button[aria-controls='lists']"
            Vitest.expect(page.Query ".map-column").toHaveClass "is-hidden-mobile"
            Vitest.expect(page.Query "button[aria-controls='lists']").toHaveAttribute ("aria-pressed", "true")
            Vitest.expect(page.Query "#stop-select").toHaveValue "G29"
        }
)

Vitest.test (
    "the standalone production HTML boots and renders arrivals",
    fun () ->
        promise {
            let html = readFile "dist/index.html" "utf8"

            let script =
                Regex.Match(html, "<script[^>]*type=\"module\"[^>]*>([\\s\\S]*?)</script>")

            Vitest.expect(script.Success).toBe true

            let dom =
                createDom html (createObj [ "url" ==> "https://example.com/#G26"; "runScripts" ==> "outside-only" ])

            let now = nowMilliseconds () |> millisecondsToSeconds

            let buffer =
                encodeAt
                    now
                    [| trip
                           "smoke"
                           "smoke"
                           0
                           [| {| stopId = "G26N"
                                 arrival = {| time = float (now + 120.0<seconds>) |} |}
                              {| stopId = "G22N"
                                 arrival = {| time = float (now + 360.0<seconds>) |} |} |] |]

            let fetch =
                vi.fn (fun (_: string) -> JS.Constructors.Promise.resolve (response buffer 200))
            // Keep a real-clock startup smoke test alongside the controlled-clock flows.
            dom.window?fetch <- fetch

            try
                dom.window.eval script.Groups[1].Value

                do!
                    RTL.waitFor (
                        (fun () ->
                            Vitest.expect(dom.window.document.querySelector("#lists").textContent).toContain "Court Sq"),
                        RTLWaitForOptions(container = dom.window.document.body)
                    )

                Vitest.expect(fetch).toHaveBeenCalledTimes 1
                Vitest.expect(dom.window.document.querySelector ("#stop-select")).toHaveValue "G26"
            finally
                dom.window.close ()
        }
)
