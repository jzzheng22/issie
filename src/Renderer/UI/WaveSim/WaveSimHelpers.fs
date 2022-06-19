module WaveSimHelpers

open Fulma
open Fable.React
open Fable.React.Props

open CommonTypes
open ModelType
open DiagramStyle
open FileMenuView
open SimulatorTypes
open NumberHelpers
open DrawModelType
open Sheet.SheetInterface

/// Determines whether a clock cycle is generated with a vertical bar at the beginning,
/// denoting that a waveform changes value at the start of that clock cycle. NB this
/// does not determine whether a waveform changes value at the end of that clock cycle.
type BinaryTransition =
    | ZeroToZero
    | ZeroToOne
    | OneToZero
    | OneToOne

/// Determines whether a non-binary waveform changes value at the beginning of that clock cycle.
type NonBinaryTransition =
    | Change
    | Const

/// Waveforms can be either binary or non-binary; these have different properties.
type Transition =
    | BinaryTransition of BinaryTransition
    | NonBinaryTransition of NonBinaryTransition

module Constants = 
    let nonBinaryTransLen : float = 0.2

    let viewBoxHeight : float = 1.0

    /// Height of a waveform
    let waveHeight : float = 0.8
    let spacing : float = (viewBoxHeight - waveHeight) / 2.

    let yTop = spacing
    let yBot = waveHeight + spacing

    /// TODO: Remove this limit. This stops the waveform simulator moving past 500 clock cycles.
    let maxLastClk = 500

    /// TODO: Use geometric sequence parametrised by startZoom, endZoom, numZoomLevels
    let zoomLevels = [|
    //   0     1     2    3     4     5    6    7    8    9    10   11    12   13   14   15
        0.25; 0.33; 0.5; 0.67; 0.75; 0.8; 0.9; 1.0; 1.1; 1.5; 1.75; 2.0; 2.50; 3.0; 4.0; 5.0;
    |]



let viewBoxMinX m = string (float m.StartCycle * m.ZoomLevel)
let viewBoxWidth m = string (float m.ShownCycles * m.ZoomLevel)

let endCycle wsModel = wsModel.StartCycle + wsModel.ShownCycles - 1

let singleWaveWidth wsModel = 30.0 * wsModel.ZoomLevel

let button options func label = Button.button (List.append options [ Button.OnClick func ]) [ label ]

let selectedWaves (wsModel: WaveSimModel) : Map<string, Wave> = Map.filter (fun _ key -> key.Selected) wsModel.AllWaves
let selectedWavesCount (wsModel: WaveSimModel) = Map.count (selectedWaves wsModel)

let pointsToString (points: XYPos list) : string =
    List.fold (fun str (point: XYPos) ->
        str + string point.X + "," + string point.Y + " "
    ) "" points

/// Retrieve value of wave at given clock cycle as an int.
let getWaveValue (currClkCycle: int) (wave: Wave): int64 =
    List.tryItem currClkCycle wave.WaveValues
    |> function
    | Some wireData ->
        convertWireDataToInt wireData
    | None ->
        // TODO: Find better default value here
        // TODO: Should probably make it so that you can't call this function in the first place.
        printf "Trying to access index %A in wave %A. Default to 0." currClkCycle wave.DisplayName
        0

/// Make left and right x-coordinates for a clock cycle.
let makeXCoords (clkCycleWidth: float) (clkCycle: int) (transition: Transition) =
    match transition with
    | BinaryTransition _ ->
        float clkCycle * clkCycleWidth, float (clkCycle + 1) * clkCycleWidth
    | NonBinaryTransition _ ->
        // These are left-shifted by nonBinaryTransLen: doing this means that for non-binary
        // waveforms, only the transition at the start of each cycle needs to be considered,
        // rather than the transition at both the start and end of each cycle.
        float clkCycle * clkCycleWidth - Constants.nonBinaryTransLen,
        float (clkCycle + 1) * clkCycleWidth - Constants.nonBinaryTransLen

/// Make top-left, top-right, bottom-left, bottom-right coordinates for a clock cycle.
let makeCoords (clkCycleWidth: float) (clkCycle: int) (transition: Transition) : XYPos * XYPos * XYPos * XYPos =
    let xLeft, xRight = makeXCoords clkCycleWidth clkCycle transition

    let topL = {X = xLeft; Y = Constants.yTop}
    let topR = {X = xRight; Y = Constants.yTop}
    let botL = {X = xLeft; Y = Constants.yBot}
    let botR = {X = xRight; Y = Constants.yBot}

    topL, topR, botL, botR

/// Generate points for a binary waveform
let binaryWavePoints (clkCycleWidth: float) (clkCycle: int) (transition: BinaryTransition)  : XYPos list * int =
    let topL, topR, botL, botR = makeCoords clkCycleWidth clkCycle (BinaryTransition transition)
    // Each match condition generates a specific transition type
    match transition with
    | ZeroToZero ->
        [botL; botR], clkCycle + 1
    | ZeroToOne ->
        [botL; topL; topR], clkCycle + 1
    | OneToZero ->
        [topL; botL; botR], clkCycle + 1
    | OneToOne ->
        [topL; topR], clkCycle + 1

/// TODO: Account for very low zoom levels.
/// TODO: Consider: If singleWaveWidth M nonBinaryTransLen, then show crosshatch.
/// Generate points for a non-binary waveform.
let nonBinaryWavePoints (clkCycleWidth: float) (clkCycle: int) (transition: NonBinaryTransition) : (XYPos list * XYPos list) * int =
    let xLeft, _ = makeXCoords clkCycleWidth clkCycle (NonBinaryTransition transition)
    let topL, topR, botL, botR = makeCoords clkCycleWidth clkCycle (NonBinaryTransition transition)

    let crossHatchMid = {X = xLeft + Constants.nonBinaryTransLen; Y = 0.5}
    let crossHatchTop = {X = xLeft + Constants.nonBinaryTransLen * 2.; Y = Constants.yTop}
    let crossHatchBot = {X = xLeft + Constants.nonBinaryTransLen * 2.; Y = Constants.yBot}

    match transition with
    // This needs to account for different zoom levels:
    // Can probably just look at screen size and zoom level
    // And then scale the horizontal part accordingly
    // When zoomed out sufficiently and values changing fast enough,
    // The horizontal part will have length zero.
    // May need to account for odd/even clock cycle
    | Change ->
        let topStart = [topL; crossHatchMid; crossHatchTop; topR]
        let botStart = [botL; crossHatchMid; crossHatchBot; botR]
        (topStart, botStart), clkCycle + 1
    | Const ->
        ([topL; topR], [botL; botR]), clkCycle + 1

/// Determine transitions for each clock cycle of a binary waveform.
/// Assumes that waveValues starts at clock cycle 0.
let calculateBinaryTransitions (waveValues: Bit list list) : BinaryTransition list =
    [List.head waveValues] @ waveValues
    |> List.pairwise
    |> List.map (fun (x, y) ->
        match x, y with
        | [Zero], [Zero] -> ZeroToZero
        | [Zero], [One] -> ZeroToOne
        | [One], [Zero] -> OneToZero
        | [One], [One] -> OneToOne
        | _ ->
            failwithf "Unrecognised transition"
    )

/// Determine transitions for each clock cycle of a non-binary waveform.
/// Assumes that waveValues starts at clock cycle 0.
let calculateNonBinaryTransitions (waveValues: Bit list list) : NonBinaryTransition list =
    // TODO: See if this will break if the clock cycle isn't 0.
    // Concat [[]] so first clock cycle always starts with Change
    [[]] @ waveValues
    |> List.pairwise
    |> List.map (fun (x, y) ->
        if x = y then
            Const
        else
            Change
    )

let isWaveSelected (waveSimModel: WaveSimModel) (name: string) : bool = waveSimModel.AllWaves[name].Selected
