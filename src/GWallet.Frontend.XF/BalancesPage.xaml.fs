﻿namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

type BalancesPage()
                      as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let theLabel = mainLayout.FindByName<Label> "theLabel"

    do
        this.Init()

    member this.UpdateLabel (label: Label) =
        let tapGestureRecognizer = TapGestureRecognizer()
        tapGestureRecognizer.Tapped.Subscribe(fun _ ->
            let receivePage = ReceivePage()
            FrontendHelpers.SwitchToNewPage this receivePage true
        ) |> ignore
        label.GestureRecognizers.Add tapGestureRecognizer


    member private this.Init () =
        Device.BeginInvokeOnMainThread(fun _ ->
            this.UpdateLabel theLabel

            // workaround for bug https://github.com/xamarin/Xamarin.Forms/issues/9526
            theLabel.TextColor <- Color.Black
        )
