# Inline Lambda Event Handlers - Complete Guide

## The Question
Can I use inline lambdas for event handlers?
```csharp
_engine.DncConnectionChanged += (sender, e) => 
    Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
```

**Answer:** YES, but you can't unsubscribe easily!

---

## Solution 1: Store Lambda in Field ✅ BEST

```csharp
public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;
    
    // Store lambda as field
    private EventHandler<DncConnectionEventArgs>? _dncConnectionHandler;
    
    public MainWindow()
    {
        InitializeComponent();
        _engine = new DnbEngine();
        
        // Create lambda and store it
        _dncConnectionHandler = (sender, e) => 
            Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
        
        // Subscribe using the stored lambda
        _engine.DncConnectionChanged += _dncConnectionHandler;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // ✅ Can unsubscribe using the stored reference!
        _engine.DncConnectionChanged -= _dncConnectionHandler;
        
        await _engine.StopAsync();
        _engine.Dispose();
    }
}
```

**Pros:**
- ✅ Can unsubscribe properly
- ✅ Still concise (lambda syntax)
- ✅ Clear field name documents purpose

**Cons:**
- ⚠️ Extra field for each event
- ⚠️ More lines of code

---

## Solution 2: Named Method ✅ YOUR CURRENT APPROACH

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        _engine.DncConnectionChanged += Engine_DncConnectionChanged;
    }
    
    private void Engine_DncConnectionChanged(object? sender, DncConnectionEventArgs e)
    {
        Dispatcher.BeginInvoke(() => 
            IsDncConnectedCheckBox.IsChecked = e.IsConnected);
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _engine.DncConnectionChanged -= Engine_DncConnectionChanged;  // ✅ Easy!
    }
}
```

**Pros:**
- ✅ Easy to unsubscribe
- ✅ Can add breakpoints
- ✅ Can add complex logic easily
- ✅ Reusable method

**Cons:**
- ⚠️ More lines of code
- ⚠️ Method name needed

---

## Solution 3: Inline Lambda (No Unsubscribe) ⚠️

```csharp
public MainWindow()
{
    // Subscribe inline
    _engine.DncConnectionChanged += (sender, e) => 
        Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
}

private async void Window_Closing(object? sender, CancelEventArgs e)
{
    // ❌ CAN'T unsubscribe!
    // Just dispose and hope for the best
    await _engine.StopAsync();
    _engine.Dispose();
}
```

**When this is OK:**
- ✅ Subscriber and publisher have same lifetime
- ✅ Simple apps with single window
- ✅ Short-lived applications
- ✅ Prototypes/demos

**When this is RISKY:**
- ❌ Long-running applications
- ❌ Multiple windows
- ❌ Shared/static event sources
- ❌ Memory leak concerns

---

## Solution 4: Weak Event Pattern ✅ ADVANCED

```csharp
using System.Windows;

public MainWindow()
{
    // Weak event - no need to unsubscribe!
    WeakEventManager<DnbEngine, DncConnectionEventArgs>
        .AddHandler(_engine, nameof(DnbEngine.DncConnectionChanged), Engine_DncConnectionChanged);
}

private void Engine_DncConnectionChanged(object? sender, DncConnectionEventArgs e)
{
    Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
}

// Window_Closing - no unsubscribe needed!
```

**Pros:**
- ✅ No memory leaks even without unsubscribe
- ✅ Weak reference allows GC
- ✅ WPF built-in pattern

**Cons:**
- ⚠️ More complex syntax
- ⚠️ WPF-specific (not portable)
- ⚠️ Slight performance overhead

---

## Comparison Table

| Solution | Unsubscribe | Memory Safe | Code Lines | Readability |
|----------|-------------|-------------|------------|-------------|
| **Stored Lambda** | ✅ Yes | ✅ Yes | Medium | Good |
| **Named Method** | ✅ Yes | ✅ Yes | More | Best |
| **Inline Lambda** | ❌ No | ⚠️ Depends | Fewest | Good |
| **Weak Event** | N/A | ✅ Yes | Medium | Complex |

---

## Real Code Examples

### Example 1: All Events with Stored Lambdas

```csharp
public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;
    
    // Store all lambdas as fields
    private EventHandler<LogEventArgs>? _logHandler;
    private EventHandler<DncConnectionEventArgs>? _dncConnectionHandler;
    private EventHandler<ScadaConnectionEventArgs>? _scadaConnectionHandler;
    private EventHandler<CommandReceivedEventArgs>? _dncCommandHandler;
    private EventHandler<ScadaDataEventArgs>? _scadaDataHandler;
    
    public MainWindow()
    {
        InitializeComponent();
        _engine = new DnbEngine();
        
        // Create and subscribe all lambdas
        _logHandler = (s, e) => Dispatcher.BeginInvoke(() => {
            LogTextBox.AppendText(e.ToString() + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
        
        _dncConnectionHandler = (s, e) => 
            Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
        
        _scadaConnectionHandler = (s, e) => 
            Dispatcher.BeginInvoke(() => IsScadaConnectedCheckBox.IsChecked = e.IsConnected);
        
        _dncCommandHandler = (s, e) => Dispatcher.BeginInvoke(() => {
            DncTrafficTextBox.AppendText(e.ToString() + Environment.NewLine);
            DncTrafficTextBox.ScrollToEnd();
        });
        
        _scadaDataHandler = (s, e) => Dispatcher.BeginInvoke(() => {
            ScadaTrafficTextBox.AppendText(e.ToString() + Environment.NewLine);
            ScadaTrafficTextBox.ScrollToEnd();
        });
        
        _engine.LogMessage += _logHandler;
        _engine.DncConnectionChanged += _dncConnectionHandler;
        _engine.ScadaConnectionChanged += _scadaConnectionHandler;
        _engine.DncCommandReceived += _dncCommandHandler;
        _engine.ScadaDataReceived += _scadaDataHandler;
    }
    
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // ✅ Unsubscribe all
        _engine.LogMessage -= _logHandler;
        _engine.DncConnectionChanged -= _dncConnectionHandler;
        _engine.ScadaConnectionChanged -= _scadaConnectionHandler;
        _engine.DncCommandReceived -= _dncCommandHandler;
        _engine.ScadaDataReceived -= _scadaDataHandler;
        
        await _engine.StopAsync();
        _engine.Dispose();
    }
}
```

**Verdict:** Works, but VERBOSE! Not better than named methods.

---

### Example 2: Mix & Match (Practical)

```csharp
public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;
    
    // Only store lambda for simple one-liners
    private EventHandler<DncConnectionEventArgs>? _dncConnectionHandler;
    
    public MainWindow()
    {
        InitializeComponent();
        _engine = new DnbEngine();
        
        // Named method for complex logic
        _engine.LogMessage += Engine_LogMessage;
        _engine.DncCommandReceived += Engine_DncCommandReceived;
        _engine.ScadaDataReceived += Engine_ScadaDataReceived;
        
        // Lambda for simple one-liners
        _dncConnectionHandler = (s, e) => 
            Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
        _engine.DncConnectionChanged += _dncConnectionHandler;
        
        _engine.ScadaConnectionChanged += (s, e) =>  // ⚠️ Can't unsubscribe this one!
            Dispatcher.BeginInvoke(() => IsScadaConnectedCheckBox.IsChecked = e.IsConnected);
    }
    
    private void Engine_LogMessage(object? sender, LogEventArgs e)
    {
        Dispatcher.BeginInvoke(() => {
            LogTextBox.AppendText(e.ToString() + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }
    
    // ... other handlers ...
}
```

**Verdict:** Flexible, but inconsistent. Pick ONE pattern!

---

## Recommendation for YOUR DNBridge Project

### Current Code (Named Methods) ✅ BEST CHOICE

```csharp
_engine.DncConnectionChanged += Engine_DncConnectionChanged;

private void Engine_DncConnectionChanged(object? sender, DncConnectionEventArgs e)
{
    Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
}
```

**Why this is better than inline lambdas:**
1. ✅ **Easy unsubscribe** - Just use `-=` with method name
2. ✅ **Debuggable** - Can set breakpoints on method
3. ✅ **Testable** - Can test method independently
4. ✅ **Discoverable** - Easy to find with "Go to Definition"
5. ✅ **Consistent** - Same pattern for all events
6. ✅ **Refactorable** - IDE refactoring tools work better

---

## When to Use Each Pattern

### Use Named Methods When:
- ✅ You need to unsubscribe (most cases)
- ✅ Handler has more than 1-2 lines
- ✅ Professional/production code
- ✅ Team development
- ✅ **Your DNBridge project** ← Stay with this!

### Use Stored Lambdas When:
- ✅ Handler captures local variables
- ✅ Quick prototyping
- ✅ Very simple one-liners
- ✅ Need closure over local state

### Use Inline Lambdas When:
- ✅ Very short-lived subscriptions
- ✅ Prototypes/demos
- ✅ You're 100% sure no unsubscribe needed
- ⚠️ Use sparingly in production!

### Use Weak Events When:
- ✅ Long-lived publishers
- ✅ Many short-lived subscribers
- ✅ Memory-critical applications
- ✅ Can't guarantee unsubscribe

---

## Common Mistakes

### ❌ Mistake 1: Trying to Unsubscribe Inline Lambda

```csharp
// Subscribe
_engine.DncConnectionChanged += (s, e) => DoSomething();

// Try to unsubscribe - DOESN'T WORK!
_engine.DncConnectionChanged -= (s, e) => DoSomething();  // ❌ Still subscribed!
```

### ❌ Mistake 2: Forgetting to Store Lambda

```csharp
// Need to unsubscribe later, but didn't store reference
_engine.DncConnectionChanged += (s, e) => DoSomething();

// Later... can't unsubscribe! ❌
```

### ❌ Mistake 3: Capturing Wrong Variables

```csharp
for (int i = 0; i < 5; i++)
{
    // ❌ BAD: All lambdas capture the SAME 'i' variable!
    _engine.DncConnectionChanged += (s, e) => Console.WriteLine(i);  
}
// All handlers will print '5' (final value)!

// ✅ GOOD: Capture loop variable properly
for (int i = 0; i < 5; i++)
{
    int captured = i;  // Capture in local variable
    _engine.DncConnectionChanged += (s, e) => Console.WriteLine(captured);
}
```

---

## Performance Considerations

### Lambda Overhead

```csharp
// Creates closure object if captures variables
int count = 0;
_engine.DncConnectionChanged += (s, e) => count++;  // Allocates closure

// No closure if doesn't capture
_engine.DncConnectionChanged += (s, e) => Console.WriteLine("test");  // No allocation
```

**Verdict:** Performance difference is negligible for UI apps like yours!

---

## Summary for Your Project

### Your Question:
> Can I handle events inline: `_engine.DncConnectionChanged += (sender, e) => ...`

**Answer:**
- ✅ **Technically:** YES, it works perfectly
- ⚠️ **Practically:** You can't unsubscribe easily
- 🎯 **Recommendation:** Stick with named methods (your current approach)

### Keep Your Current Code! ✅

```csharp
// What you have now - BEST approach for your project:
_engine.DncConnectionChanged += Engine_DncConnectionChanged;

private void Engine_DncConnectionChanged(object? sender, DncConnectionEventArgs e)
{
    Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
}

// Cleanup:
_engine.DncConnectionChanged -= Engine_DncConnectionChanged;  // Easy! ✅
```

**Don't change it to inline lambdas!** Your current approach is professional and maintainable.

---

## If You REALLY Want Inline Lambdas

### Option A: Store in fields (if you insist)

```csharp
private EventHandler<DncConnectionEventArgs>? _dncHandler;

public MainWindow()
{
    _dncHandler = (s, e) => Dispatcher.BeginInvoke(() => 
        IsDncConnectedCheckBox.IsChecked = e.IsConnected);
    _engine.DncConnectionChanged += _dncHandler;
}

private void Window_Closing(...)
{
    _engine.DncConnectionChanged -= _dncHandler;  // ✅ Works
}
```

### Option B: Don't unsubscribe (risky)

```csharp
public MainWindow()
{
    // Just subscribe, never unsubscribe
    _engine.DncConnectionChanged += (s, e) => Dispatcher.BeginInvoke(() => 
        IsDncConnectedCheckBox.IsChecked = e.IsConnected);
}

// Window_Closing - no unsubscribe
// ⚠️ OK only because _engine is owned by MainWindow
```

**My recommendation:** **DON'T DO THIS!** Keep your named methods! 🎯
