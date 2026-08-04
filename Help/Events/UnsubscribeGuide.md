# Event Unsubscription - When and Why

## The Memory Leak Problem

### How Event Subscriptions Create References

```csharp
// When you subscribe:
_engine.DncConnectionChanged += Engine_DncConnectionChanged;

// What actually happens:
_engine._dncConnectionChanged = (EventHandler<DncConnectionEventArgs>)
    Delegate.Combine(
        _engine._dncConnectionChanged,
        new EventHandler<DncConnectionEventArgs>(this.Engine_DncConnectionChanged)
    );
```

**Result:** The `_engine` now has a delegate that holds a reference to `this` (MainWindow)!

```
┌─────────────┐  owns   ┌──────────────┐
│ MainWindow  │────────▶│  DnbEngine   │
│             │         │              │
└─────────────┘         └──────────────┘
       ▲                       │
       │   event delegate      │
       └───────────────────────┘
          keeps MainWindow alive!
```

---

## SCENARIO 1: Your Original Code (No Unsubscribe)

```csharp
public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;  // Instance field
    
    public MainWindow()
    {
        _engine = new DnbEngine();  // Creates NEW engine for this window
        _engine.DncConnectionChanged += Engine_DncConnectionChanged;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _engine.Dispose();
        // No unsubscribe
    }
}
```

### Memory Lifecycle:

```
Time: 0ms
  MainWindow created
     ├─ _engine created (owned by MainWindow)
     └─ Subscribe to _engine.DncConnectionChanged
  
Time: 5000ms (User closes window)
  Window_Closing called
     ├─ _engine.Dispose() called
     └─ Window closes
  
Time: 5001ms
  MainWindow reference count: 0
  _engine reference count: 0
  
Time: 5010ms (GC runs)
  ✅ Both MainWindow and _engine are collected together
  ✅ No memory leak!
```

**Why it works:**
- MainWindow owns _engine (private field)
- When MainWindow dies, _engine also dies
- The circular reference doesn't matter because both are unreachable
- Garbage Collector cleans up both

**Verdict:** ✅ **Works, but...**

---

## SCENARIO 2: Shared Engine (Memory Leak!)

```csharp
public class EngineManager
{
    public static DnbEngine SharedEngine { get; } = new DnbEngine();
}

public partial class MainWindow : Window
{
    private DnbEngine _engine;
    
    public MainWindow()
    {
        _engine = EngineManager.SharedEngine;  // Get SHARED instance
        _engine.DncConnectionChanged += Engine_DncConnectionChanged;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _engine.Dispose();
        // ❌ NO UNSUBSCRIBE!
    }
}
```

### Memory Lifecycle:

```
Time: 0ms
  MainWindow created
     └─ Subscribe to SharedEngine.DncConnectionChanged
     
  EngineManager.SharedEngine still alive (static!)
     └─ Has delegate pointing to MainWindow.Engine_DncConnectionChanged
  
Time: 5000ms (User closes window)
  Window_Closing called
     └─ Window closes
  
Time: 5001ms
  MainWindow reference count: 1 ⚠️
     └─ SharedEngine.DncConnectionChanged delegate still references it!
  
Time: Forever
  💀 MEMORY LEAK! MainWindow never collected
  💀 All its resources (TextBoxes, CheckBoxes, etc.) stuck in memory
```

**Why it leaks:**
- SharedEngine lives forever (static)
- Delegate holds reference to MainWindow
- MainWindow can never be collected

**Verdict:** ❌ **MEMORY LEAK!**

---

## SCENARIO 3: Multiple Windows (Memory Leak!)

```csharp
public partial class MainWindow : Window
{
    private static DnbEngine _sharedEngine = new DnbEngine();
    
    public MainWindow()
    {
        _sharedEngine.DncConnectionChanged += Engine_DncConnectionChanged;
    }
    
    private void OpenNewWindow_Click(object sender, RoutedEventArgs e)
    {
        var newWindow = new MainWindow();  // Creates ANOTHER window
        newWindow.Show();
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // ❌ NO UNSUBSCRIBE!
        _sharedEngine.Dispose();
    }
}
```

### Memory Lifecycle:

```
Time: 0ms
  Window1 created
     └─ Subscribes to _sharedEngine
  
Time: 1000ms (User opens another window)
  Window2 created
     └─ Subscribes to _sharedEngine (2nd subscriber!)
  
Time: 2000ms (User closes Window1)
  Window1.Window_Closing called
     └─ Window1 closes
  
Time: 2001ms
  Window1 reference count: 1 ⚠️
     └─ _sharedEngine still has delegate to Window1!
  
Time: Forever
  💀 Window1 leaked in memory!
  ✅ Window2 still works (still subscribed)
```

**Verdict:** ❌ **MEMORY LEAK!**

---

## THE FIX: Always Unsubscribe

```csharp
public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;
    
    public MainWindow()
    {
        _engine = new DnbEngine();
        
        // Subscribe
        _engine.LogMessage += Engine_LogMessage;
        _engine.DncConnectionChanged += Engine_DncConnectionChanged;
        _engine.ScadaConnectionChanged += Engine_ScadaConnectionChanged;
        _engine.DncCommandReceived += Engine_DncCommandReceived;
        _engine.ScadaDataReceived += Engine_ScadaDataReceived;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // ✅ UNSUBSCRIBE - Break the references!
        _engine.LogMessage -= Engine_LogMessage;
        _engine.DncConnectionChanged -= Engine_DncConnectionChanged;
        _engine.ScadaConnectionChanged -= Engine_ScadaConnectionChanged;
        _engine.DncCommandReceived -= Engine_DncCommandReceived;
        _engine.ScadaDataReceived -= Engine_ScadaDataReceived;
        
        _cts?.Cancel();
        await _engine.StopAsync();
        _engine.Dispose();
    }
}
```

### Memory Lifecycle (Fixed):

```
Time: 0ms
  MainWindow created
     └─ Subscribe to events
  
Time: 5000ms (User closes window)
  Window_Closing called
     ├─ Unsubscribe from all events ✅
     ├─ Engine no longer references MainWindow ✅
     └─ Window closes
  
Time: 5001ms
  MainWindow reference count: 0 ✅
  
Time: 5010ms (GC runs)
  ✅ MainWindow collected properly
  ✅ No memory leak!
```

---

## BEST PRACTICES

### 1. Always Mirror Subscribe/Unsubscribe

```csharp
// Subscribe in constructor or OnLoaded
public MainWindow()
{
    _engine.EventA += Handler_A;
    _engine.EventB += Handler_B;
}

// Unsubscribe in Closing or Dispose
private void Window_Closing(object? sender, CancelEventArgs e)
{
    _engine.EventA -= Handler_A;
    _engine.EventB -= Handler_B;
}
```

### 2. Use Weak Events for Long-Lived Publishers

```csharp
// Instead of:
_engine.DncConnectionChanged += Engine_DncConnectionChanged;

// Use weak event pattern (WPF):
WeakEventManager<DnbEngine, DncConnectionEventArgs>
    .AddHandler(_engine, nameof(DnbEngine.DncConnectionChanged), Engine_DncConnectionChanged);

// No need to unsubscribe! Weak reference allows MainWindow to be GC'd
```

### 3. Use IDisposable Pattern

```csharp
public partial class MainWindow : Window, IDisposable
{
    private bool _disposed;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        // Unsubscribe
        _engine.LogMessage -= Engine_LogMessage;
        _engine.DncConnectionChanged -= Engine_DncConnectionChanged;
        
        // Dispose resources
        _engine?.Dispose();
        _cts?.Dispose();
        
        _disposed = true;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        Dispose();
    }
}
```

### 4. Use using statement for short-lived subscriptions

```csharp
// Create a subscription that auto-unsubscribes
public IDisposable Subscribe(Action<DncConnectionEventArgs> handler)
{
    EventHandler<DncConnectionEventArgs> eventHandler = (s, e) => handler(e);
    DncConnectionChanged += eventHandler;
    
    return new Subscription(() => DncConnectionChanged -= eventHandler);
}

// Usage:
using (var subscription = _engine.Subscribe(e => Console.WriteLine(e.IsConnected)))
{
    // While in scope, subscribed
    await _engine.StartAsync();
}
// Auto-unsubscribed when leaving scope!

class Subscription : IDisposable
{
    private readonly Action _unsubscribe;
    public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;
    public void Dispose() => _unsubscribe();
}
```

---

## WHEN DO YOU NEED TO UNSUBSCRIBE?

### ✅ MUST Unsubscribe:
- Subscriber has shorter lifetime than publisher
- Shared/static/singleton event publishers
- Multiple windows subscribing to same engine
- Long-running applications (memory adds up)
- Mobile/resource-constrained environments

### ⚠️ OPTIONAL (but good practice):
- Subscriber and publisher die together (your original case)
- Simple desktop apps with single window
- Short-lived applications
- Prototypes and testing

### ❌ DON'T NEED:
- Lambda subscriptions that will never be unsubscribed anyway
- Static event handlers (live forever anyway)
- When both objects are being Disposed together

---

## YOUR DNBRIDGE PROJECT

### Original Code Analysis:
```csharp
private readonly DnbEngine _engine;  // Private instance

public MainWindow()
{
    _engine = new DnbEngine();  // NEW instance, owned by MainWindow
    _engine.DncConnectionChanged += Engine_DncConnectionChanged;
}
```

**Technical verdict:** ✅ Works fine, no memory leak

**Professional verdict:** ⚠️ Should unsubscribe anyway

### Why Add Unsubscribing?

1. **Best Practice**: Industry standard, shows professionalism
2. **Future-Proof**: What if you later make _engine static?
3. **Code Reviews**: Reviewers will ask about it
4. **Maintenance**: Next developer knows intent is to clean up
5. **Habit**: Good habit for when you DO need it

### The Fix (Already Applied):

```csharp
private async void Window_Closing(object? sender, CancelEventArgs e)
{
    // Unsubscribe from all events
    _engine.LogMessage -= Engine_LogMessage;
    _engine.DncConnectionChanged -= Engine_DncConnectionChanged;
    _engine.ScadaConnectionChanged -= Engine_ScadaConnectionChanged;
    _engine.DncCommandReceived -= Engine_DncCommandReceived;
    _engine.ScadaDataReceived -= Engine_ScadaDataReceived;

    _cts?.Cancel();
    await _engine.StopAsync();
    _engine.Dispose();
}
```

---

## DEBUGGING MEMORY LEAKS

### Find Memory Leaks in Visual Studio:

1. Run app with debugger
2. Open window, close window, open again 10 times
3. Take memory snapshot (Debug → Take Memory Snapshot)
4. Look for multiple MainWindow instances
5. Check what's keeping them alive (event references!)

### Prevention Checklist:

- [ ] Subscribe in constructor/OnLoaded
- [ ] Unsubscribe in Closing/Dispose
- [ ] Match every += with a -=
- [ ] Consider weak events for long-lived publishers
- [ ] Test: open/close window 100 times, check memory

---

## SUMMARY

**For your DNBridge project:**

| Aspect | Before | After | Why |
|--------|--------|-------|-----|
| **Memory Safety** | Safe ✅ | Safe ✅ | Instance ownership |
| **Best Practice** | Missing ⚠️ | Good ✅ | Professional code |
| **Future-Proof** | Risky ⚠️ | Safe ✅ | Prevents future bugs |
| **Code Quality** | OK | Better | Clear intent |

**Bottom line:** Your original code worked, but the unsubscribe pattern I added is **professional best practice** and prevents future issues! ✅
