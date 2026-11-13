module Main

open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Ui

importSideEffects "./bulma.scss"
importSideEffects "./style.css"

try
    Program.mkProgram App.init App.update view
    |> Program.withSubscription App.subscriptions
    |> withReactRoot
    |> Program.run
with err ->
    JS.console.error err
    setError "Failed to load stops list."
