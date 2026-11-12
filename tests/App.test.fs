module Tests.AppTests

open System
open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Vitest
open Tests.Fixtures
open Time

Vitest.beforeEach (fun () -> resetBrowser "")

Vitest.test (
    "shows destination headings and separates short-turn trains in the same direction",
    fun () ->
        promise {
            let buffer =
                encode
                    [| trip "north-1" "north-1" 0 [| call "G26N" 120.0<seconds>; call "G22N" 360.0<seconds> |]
                       trip "short" "short" 0 [| call "G26N" 180.0<seconds>; call "G24N" 300.0<seconds> |]
                       trip "north-2" "north-2" 0 [| call "G26N" 240.0<seconds>; call "G22N" 480.0<seconds> |]
                       trip "south" "south" 1 [| call "G26S" 420.0<seconds>; call "F27S" 1800.0<seconds> |] |]

            mockFeed buffer |> ignore
            renderApp () |> ignore
            do! waitForArrivals ()

            let court =
                RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Northbound to Court Sq"))

            let short =
                RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Northbound to 21 St"))

            let church =
                RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Southbound to Church Av"))

            Vitest.expect(court.textContent).toBe "Court Sq"
            Vitest.expect(short.textContent).toBe "21 St"
            Vitest.expect(church.textContent).toBe "Church Av"
            Vitest.expect(court.closest(".card").Value.querySelectorAll(".arrival").length).toBe 2
            Vitest.expect(short.closest(".card").Value.querySelectorAll(".arrival").length).toBe 1

            Vitest
                .expect(short.closest(".card").Value.querySelector (".arrival"))
                .toHaveAttribute ("data-trip-id", "short")
        }
)

Vitest.test (
    "switches mobile views without resetting the station or fetching again",
    fun () ->
        promise {
            let fetch = mockFeed (trainsFeed ())
            renderApp () |> ignore
            do! waitForArrivals ()

            let arrivals =
                RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Arrivals"))

            let map = RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Map"))
            let arrivalsColumn = document.querySelector ".arrivals-column"
            let mapColumn = document.querySelector ".map-column"
            Vitest.expect(arrivals).toHaveAttribute ("aria-pressed", "true")
            Vitest.expect(map).toHaveAttribute ("aria-pressed", "false")
            Vitest.expect(mapColumn).toHaveClass "is-hidden-mobile"
            Vitest.expect(arrivalsColumn.classList.contains "is-hidden-mobile").toBe false

            Vitest.expect(RTL.screen.getByRole ("group", ByRoleOptions(name = Text "View"))).toHaveClass
                "is-hidden-tablet"

            RTL.fireEvent.click map
            Vitest.expect(map).toHaveAttribute ("aria-pressed", "true")
            Vitest.expect(arrivals).toHaveAttribute ("aria-pressed", "false")
            Vitest.expect(arrivalsColumn).toHaveClass "is-hidden-mobile"
            Vitest.expect(mapColumn.classList.contains "is-hidden-mobile").toBe false
            Vitest.expect(fetch).toHaveBeenCalledTimes 1

            // Station selection and refreshes must not switch the view back.
            RTL.fireEvent.click (document.querySelector "[data-stop-id='G24']" :?> Browser.Types.HTMLElement)
            do! RTL.waitFor (fun () -> Vitest.expect(stationSelect ()).toHaveValue "G24")
            do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)
            do! advance 5000<milliseconds>
            do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 3)
            Vitest.expect(arrivalsColumn).toHaveClass "is-hidden-mobile"
            Vitest.expect(mapColumn.classList.contains "is-hidden-mobile").toBe false

            RTL.fireEvent.click arrivals
            Vitest.expect(mapColumn).toHaveClass "is-hidden-mobile"
            Vitest.expect(arrivalsColumn.classList.contains "is-hidden-mobile").toBe false
            Vitest.expect(arrivals).toHaveAttribute ("aria-pressed", "true")
            Vitest.expect(stationSelect ()).toHaveValue "G24"
            Vitest.expect(window.location.hash).toBe "#G24"
            Vitest.expect(fetch).toHaveBeenCalledTimes 3
        }
)

Vitest.test (
    "renders arrivals, updates countdowns, and refreshes after a failed fetch",
    fun () ->
        promise {
            let buffer = arrivalsFeed ()
            let mutable failNext = false

            let fetch =
                vi.fn (fun (_: string) ->
                    let status = if failNext then 500 else 200
                    failNext <- false
                    JS.Constructors.Promise.resolve (response buffer status))

            vi.stubGlobal ("fetch", fetch)
            renderApp () |> ignore
            do! waitForArrivals ()

            Vitest.expect(fetch).toHaveBeenCalledTimes 1

            Vitest
                .expect(RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Southbound to Greenpoint Av")))
                .toHaveTextContent
                "Greenpoint Av"

            let lists = document.querySelector "#lists"
            Vitest.expect(lists).toHaveTextContent "in 2 min"
            Vitest.expect(lists).toHaveTextContent "next in 3m"
            Vitest.expect(RTL.screen.getByText "7:02 AM").toBeInTheDocument ()
            Vitest.expect(stationSelect ()).toHaveValue "G26"
            Vitest.expect(stationSelect ()).toHaveDisplayValue "Greenpoint Av"
            Vitest.expect(window.location.hash).toBe "#G26"
            Vitest.expect(RTL.screen.getByRole "status").toHaveTextContent "Feed age:"

            vi.setSystemTime ((instantFromMilliseconds (fixedNowMs + 60000.0<milliseconds>)).UtcDateTime)
            do! advance 1000<milliseconds>
            do! RTL.waitFor (fun () -> Vitest.expect(lists).toHaveTextContent "<1 min")

            let arrivalTimes () =
                let times = lists.querySelectorAll ".time"
                [| for index in 0 .. times.length - 1 -> times.item(index).textContent |]

            let beforeFailure = arrivalTimes ()
            failNext <- true
            do! advance 4000<milliseconds>
            do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)
            do! RTL.waitFor (fun () -> Vitest.expect(document.querySelector "#status.error").toHaveTextContent "500")
            Vitest.expect(arrivalTimes ()).toEqual beforeFailure
            do! advance 5000<milliseconds>
            do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 3)
        }
)

Vitest.test (
    "preserves a linked station when its feed has no arrivals",
    fun () ->
        promise {
            resetBrowser "#G22"
            mockFeed (arrivalsFeed ()) |> ignore
            renderApp () |> ignore

            do!
                RTL.waitFor (fun () ->
                    Vitest.expect(RTL.screen.getByText "No arrivals in the feed.").toBeInTheDocument ())

            Vitest.expect(stationSelect ()).toHaveValue "G22"
            Vitest.expect(stationSelect ()).toHaveDisplayValue "Court Sq"
            Vitest.expect(window.location.hash).toBe "#G22"
        }
)

for latitude, longitude, id, name, hash in
    [ 40.746554, -73.943832, "G22", "Court Sq", ""
      40.744, -73.950, "G24", "21 St", ""
      40.644, -73.980, "F27", "Church Av", "#unknown" ] do
    Vitest.test (
        $"selects the nearest supported station: {id}",
        fun () ->
            promise {
                resetBrowser hash
                let fetch = mockFeed (arrivalsFeed ())
                let mutable locate: GeoSuccess option = None

                mockLocation (
                    Some(fun success _ options ->
                        Vitest.expect(options.timeout).toBe 8000
                        locate <- Some success)
                )

                renderApp () |> ignore
                do! waitForArrivals ()
                Vitest.expect(stationSelect ()).toHaveValue "G26"

                RTL.act (fun () -> locate.Value(position latitude longitude))
                do! RTL.waitFor (fun () -> Vitest.expect(stationSelect ()).toHaveValue id)
                do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)
                Vitest.expect(stationSelect ()).toHaveDisplayValue name
                Vitest.expect(window.location.hash).toBe ("#" + id)
                Vitest.expect(RTL.screen.getByText "No arrivals in the feed.").toBeInTheDocument ()
                let marker = document.querySelector ".osm-user-location"
                Vitest.expect(marker).toHaveAttribute ("data-lat", string latitude)
            }
    )

Vitest.test (
    "a station link takes priority over device location",
    fun () ->
        promise {
            resetBrowser "#G22"
            mockFeed (arrivalsFeed ()) |> ignore
            let mutable requested = false
            mockLocation (Some(fun _ _ _ -> requested <- true))
            renderApp () |> ignore

            do!
                RTL.waitFor (fun () ->
                    Vitest.expect(RTL.screen.getByText "No arrivals in the feed.").toBeInTheDocument ())

            Vitest.expect(requested).toBe false
            Vitest.expect(stationSelect ()).toHaveValue "G22"
        }
)

Vitest.test (
    "manual selection takes priority over a pending location request",
    fun () ->
        promise {
            let fetch =
                mockFeed (
                    encode
                        [| trip "north" "north" 0 [| call "G26N" 120.0<seconds>; call "G24N" 300.0<seconds> |]
                           trip "south" "south" 1 [| call "G26S" 420.0<seconds> |] |]
                )

            let mutable locate: GeoSuccess option = None
            mockLocation (Some(fun success _ _ -> locate <- Some success))
            renderApp () |> ignore
            do! waitForArrivals ()
            selectStation "G24"
            do! RTL.waitFor (fun () -> Vitest.expect(fetch).toHaveBeenCalledTimes 2)
            RTL.act (fun () -> locate.Value(position 40.746554 -73.943832))
            do! RTL.waitFor (fun () -> Vitest.expect(document.querySelector ".osm-user-location").toBeInTheDocument ())
            Vitest.expect(stationSelect ()).toHaveValue "G24"
            Vitest.expect(window.location.hash).toBe "#G24"
            Vitest.expect(fetch).toHaveBeenCalledTimes 2
        }
)

let private failures: (string * GeoRequest) list =
    [ "permission denied", (fun _ error _ -> error (box {| code = 1 |}))
      "position unavailable", (fun _ error _ -> error (box {| code = 2 |}))
      "timeout", (fun _ error _ -> error (box {| code = 3 |}))
      "API throws", (fun _ _ _ -> failwith "unavailable")
      "invalid coordinates", (fun success _ _ -> success (position nan -73.94))
      "latitude outside its range", (fun success _ _ -> success (position 91.0 -73.94))
      "longitude outside its range", (fun success _ _ -> success (position 40.73 -181.0))
      "infinite longitude", (fun success _ _ -> success (position 40.73 infinity)) ]

for scenario, request in failures do
    Vitest.test (
        $"keeps the fallback station when geolocation fails: {scenario}",
        fun () ->
            promise {
                mockFeed (arrivalsFeed ()) |> ignore
                mockLocation (Some request)
                renderApp () |> ignore
                do! waitForArrivals ()
                Vitest.expect(stationSelect ()).toHaveValue "G26"
            }
    )

Vitest.test (
    "renders active trains, moves them along the track, and selects map stations",
    fun () ->
        promise {
            let fetch = mockFeed (trainsFeed ())
            renderApp () |> ignore
            do! waitForArrivals ()
            Vitest.expect(document.querySelector ".vec-land").toBeInTheDocument ()
            Vitest.expect(document.querySelector ".osm-track-line").toBeInTheDocument ()
            let north = document.querySelector ".osm-train[data-trip-id='G-N-1']"
            let south = document.querySelector ".osm-train[data-trip-id='123_G..S']"
            Vitest.expect(north).toHaveClass "osm-train-next"
            Vitest.expect(south).toBeInTheDocument ()
            Vitest.expect(document.querySelector ".osm-train[data-trip-id='999_G..S']").toBeNull ()
            Vitest.expect(north.querySelector "svg").toHaveClass "osm-train-svg"
            Vitest.expect(north.getAttribute "data-heading").toMatch (Text.RegularExpressions.Regex "^-?\\d")
            Vitest.expect(RTL.screen.getByText "Approaching 21 St · 2 stops away").toBeInTheDocument ()
            let latitudeBefore = float (north.getAttribute "data-lat")
            Vitest.expect(latitudeBefore).toBeGreaterThan 40.724635
            Vitest.expect(latitudeBefore).toBeLessThan 40.731352
            let stop = document.querySelector "[data-stop-id='G29']"
            Vitest.expect(stop.getAttribute "style").toMatch (Text.RegularExpressions.Regex "left:.*%;.*top:.*%")

            do! advance 30000<milliseconds>

            do!
                RTL.waitFor (fun () ->
                    Vitest.expect(float (north.getAttribute "data-lat")).toBeGreaterThan latitudeBefore)

            RTL.fireEvent.click (unbox stop)
            do! RTL.waitFor (fun () -> Vitest.expect(stationSelect ()).toHaveValue "G29")
            Vitest.expect(fetch).toHaveBeenCalled ()
        }
)
