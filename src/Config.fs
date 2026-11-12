module Config

open System
open System.Text.RegularExpressions

[<AutoOpen>]
module Prelude =
    let inline (|?) (opt: 'T option) (fallback: 'T) : 'T = Option.defaultValue fallback opt
    let inline (<|>) (a: 'T option) (b: 'T option) : 'T option = Option.orElse b a

    let inline isFinite (v: float) : bool =
        not (Double.IsNaN v || Double.IsInfinity v)

let DEFAULT_FEED_URL =
    "https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g"

let DEFAULT_STOP_ID_PREFIX = "G26"
let STOP_SUFFIX_RE = Regex("[NS]$")
let ARRIVALS_PER_DIRECTION = 3

let PREFERRED_STOP_ORDER =
    [| "F27"
       "F26"
       "F25"
       "F24"
       "F23"
       "F22"
       "F21"
       "F20"
       "A42"
       "G36"
       "G35"
       "G34"
       "G33"
       "G32"
       "G31"
       "G30"
       "G29"
       "G28"
       "G26"
       "G24"
       "G22" |]
