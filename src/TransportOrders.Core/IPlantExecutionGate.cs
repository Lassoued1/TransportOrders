namespace TransportOrders.Core;

/// <summary>A cross-process lease for automatic scheduling. Null means another driver owns the plant.</summary>
public interface IPlantExecutionGate
{
    IDisposable? TryEnter();
}
