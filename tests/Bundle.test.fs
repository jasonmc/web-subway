module Tests.BundleTests

open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Vitest
open Tests.Fixtures

[<Import("readFileSync", "node:fs")>]
let private readFile (path: string) (encoding: string) : string = jsNative

type private IWindow =
    abstract document: Browser.Types.Document
    abstract eval: string -> unit
    abstract close: unit -> unit

type private IDom = abstract window: IWindow

[<Import("JSDOM", "jsdom"); Emit("new $0($1, $2)")>]
let private createDom (html: string) (options: obj) : IDom = jsNative

Vitest.test("the standalone production HTML boots and renders arrivals", fun () -> promise {
    let html = readFile "dist/index.html" "utf8"
    let script = Regex.Match(html, "<script[^>]*type=\"module\"[^>]*>([\\s\\S]*?)</script>")
    Vitest.expect(script.Success).toBe true
    let dom = createDom html (createObj [
        "url" ==> "https://example.com/#G26"
        "runScripts" ==> "outside-only"
    ])
    let now = JS.Constructors.Date.now() / 1000.0
    let buffer = encodeAt now [|
        trip "smoke" "smoke" 0 [| {| stopId = "G26N"; arrival = {| time = now + 120.0 |} |} |]
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
