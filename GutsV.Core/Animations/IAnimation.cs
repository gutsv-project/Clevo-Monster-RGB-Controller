using System.Collections.Generic;

namespace GutsV.Core.Animations;

public interface IAnimation
{
    string Name { get; }
    IEnumerable<AnimationFrame> GenerateFrames();
}
