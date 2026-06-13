using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// A renderable unit of output. Where an <see cref="Skillz.Interaction.IPrompt{T}"/> wraps one prompt
/// shape, a view wraps one piece of UI: it is built from data through a static factory and rendered
/// with <c>console.Write(view)</c>. A view never takes the console - or any service - as a dependency,
/// so it can be rendered to any <see cref="Spectre.Console.IAnsiConsole"/> (a real terminal or a test
/// console) and asserted in isolation.
/// </summary>
internal interface IView : IRenderable;
