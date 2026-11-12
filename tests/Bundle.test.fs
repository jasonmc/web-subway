module Tests.BundleTests

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
    abstract eval: string -> unit
    abstract close: unit -> unit

type private IDom = abstract window: IWindow

[<Import("JSDOM", "jsdom"); Emit("new $0($1, $2)")>]
let private createDom (html: string) (options: obj) : IDom = jsNative

type private ManifestIcon = abstract src: string
type private Manifest =
    abstract id: string
    abstract start_url: string
    abstract scope: string
    abstract icons: ManifestIcon array

[<Emit("JSON.parse(atob($0.substring($0.indexOf(',') + 1)))")>]
let private decodeManifest (dataUrl: string) : Manifest = jsNative

Vitest.test("production metadata embeds the manifest and icons with usable launch URLs", fun () ->
    let dom = createDom (readFile "dist/index.html" "utf8") (createObj [])
    try
        let document = dom.window.document
        let href = document.querySelector("link[rel='manifest']").getAttribute "href"
        Vitest.expect(href.StartsWith "data:application/manifest+json;base64,").toBe true
        let manifest = decodeManifest href
        let siteUrl = document.querySelector("meta[property='og:url']").getAttribute "content"
        Vitest.expect(manifest.id).toBe siteUrl
        Vitest.expect(manifest.start_url).toBe siteUrl
        Vitest.expect(manifest.scope).toBe siteUrl
        Vitest.expect(manifest.icons.Length).toBe 2
        for icon in manifest.icons do
            Vitest.expect(icon.src.StartsWith "data:image/png;base64,").toBe true
        let favicons = document.querySelectorAll("link[rel='icon']")
        Vitest.expect(favicons.length).toBe 2
        for index in 0 .. favicons.length - 1 do
            let icon = favicons[index]
            Vitest.expect((icon.getAttribute "href").StartsWith "data:image/").toBe true
    finally
        dom.window.close())

Vitest.test("the standalone production HTML boots and renders arrivals", fun () -> promise {
    let html = readFile "dist/index.html" "utf8"
    let script = Regex.Match(html, "<script[^>]*type=\"module\"[^>]*>([\\s\\S]*?)</script>")
    Vitest.expect(script.Success).toBe true
    let dom = createDom html (createObj [
        "url" ==> "https://example.com/#G26"
        "runScripts" ==> "outside-only"
    ])
    let now = nowMilliseconds() |> millisecondsToSeconds
    let buffer = encodeAt now [|
        trip "smoke" "smoke" 0 [| {| stopId = "G26N"; arrival = {| time = float (now + 120.0<seconds>) |} |} |]
    |]
    let fetch = vi.fn(fun (_: string) -> JS.Constructors.Promise.resolve(response buffer 200))
    // Only this smoke test needs an isolated page. The behavioral tests use RTL.
    dom.window?fetch <- fetch
    try
        dom.window.eval script.Groups[1].Value
        do! RTL.waitFor((fun () ->
            Vitest.expect(dom.window.document.querySelector("#lists").textContent).toContain "Northbound"),
            RTLWaitForOptions(container = dom.window.document.body))
        Vitest.expect(fetch).toHaveBeenCalledTimes 1
        Vitest.expect(dom.window.document.querySelector("#stop-select")).toHaveValue "G26"
    finally
        dom.window.close()
})
