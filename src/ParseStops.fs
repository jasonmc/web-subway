module ParseStops

open System
open System.Text.RegularExpressions
open Config
open State

let private parseCsvLine (line: string) : string array =
    let rec loop i inQuotes cur acc =
        if i >= line.Length then
            List.rev (cur :: acc) |> List.toArray
        else
            match line[i], inQuotes with
            | '"', true when i + 1 < line.Length && line[i + 1] = '"' ->
                loop (i + 2) true (cur + "\"") acc
            | '"', _ -> loop (i + 1) (not inQuotes) cur acc
            | ',', false -> loop (i + 1) false "" (cur :: acc)
            | ch, _ -> loop (i + 1) inQuotes (cur + string ch) acc
    loop 0 false "" []

let private (|ValidCoord|_|) maxAbs (raw: string option) =
    match raw with
    | Some s when not (String.IsNullOrWhiteSpace s) ->
        match Double.TryParse(s.Trim()) with
        | true, v when isFinite v && abs v <= maxAbs -> Some v
        | _ -> None
    | _ -> None

let parseStops (csv: string) : StopOption array =
    match Regex.Split(csv.Trim(), @"\r?\n") with
    | [||] -> [||]
    | rows ->
        let columns = parseCsvLine rows[0]
        let tryField (fields: string array) name =
            match Array.IndexOf(columns, name) with
            | idx when idx >= 0 && idx < fields.Length && fields[idx] <> "" -> Some fields[idx]
            | _ -> None

        rows[1..]
        |> Array.filter (not << String.IsNullOrWhiteSpace)
        |> Array.choose (fun row ->
            let field = tryField (parseCsvLine row)
            match field "stop_id", field "stop_name" with
            | Some stopId, Some stopName ->
                let baseId =
                    field "parent_station"
                    |? (if field "location_type" = Some "1" then stopId else STOP_SUFFIX_RE.Replace(stopId, ""))
                let lat, lon =
                    match field "stop_lat", field "stop_lon" with
                    | ValidCoord 90.0 la, ValidCoord 180.0 lo -> Some la, Some lo
                    | _ -> None, None
                Some { id = baseId; name = stopName; latitude = lat; longitude = lon }
            | _ -> None)
        |> Array.distinctBy _.id
        |> Array.sortWith (fun a b -> String.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase))
