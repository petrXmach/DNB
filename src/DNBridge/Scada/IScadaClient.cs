using DNBridge.Elements;
using DNBridge.Events;

namespace DNBridge.Scada;

/// <summary>
/// Interface for IEC 60870-5-104 SCADA client communication.
/// </summary>
public interface IScadaClient
{
    /// <summary>
    /// True when IEC104 transport is active and STARTDT has been confirmed.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Starts (or restarts) SCADA connection management using the addresses supplied at construction.
    /// </summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>
    /// Stops reconnect loop and closes the current SCADA connection.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends IEC104 setpoint short command (C_SE_NC_1).
    /// </summary>
    void SendSetpointShort(Element104 element, double value, byte cot, bool useSbo = false);

    /// <summary>
    /// Sends IEC104 single command (C_SC_NA_1).
    /// </summary>
    void SendSingleCommand(Element104 element, bool value, byte cot, bool useSbo = false);

    /// <summary>
    /// Sends IEC104 double command (C_DC_NA_1).
    /// </summary>
    void SendDoubleCommand(Element104 element, int value, byte cot, bool useSbo = false);

    /// <summary>
    /// TEMPORARY (Main104): sends single-point information with time tag (M_SP_TB_1).
    /// </summary>
    void SendSinglePointWithTime(Element104 element, bool value, byte cot);

    /// <summary>
    /// TEMPORARY (Main104): sends double-point information with time tag (M_DP_TB_1).
    /// </summary>
    void SendDoublePointWithTime(Element104 element, int value, byte cot);

    /// <summary>
    /// TEMPORARY (Main104): sends measured short float with time tag (M_ME_TF_1).
    /// </summary>
    void SendMeasuredShortWithTime(Element104 element, double value, byte cot);

    event EventHandler<ScadaConnectionEventArgs>? ConnectionChanged;
    event EventHandler<ScadaDataEventArgs>? DataReceived;
    event EventHandler<ElementValueChangedEventArgs>? ElementsUpdated;
}
