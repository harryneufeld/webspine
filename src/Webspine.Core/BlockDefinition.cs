using System.Text;
using System.Text.Json;

namespace Webspine.Core.Composition;

public interface IBlockDefinition
{
    BlockTypeDescriptor Descriptor { get; }
    Type PayloadType { get; }
    string ImplementationDigest { get; }
    ContentEditorMetadata? Editor => null;
    object DecodeFields(Block block);
    void Validate(Block block, BlockValidationContext context);
}

// Content schema/validation only. Presentations are supplied by a selected design package.
public sealed class BlockDefinition<T>(BlockTypeDescriptor descriptor, Action<T, BlockValidationContext> validate,
    ContentEditorMetadata? editor = null) : IBlockDefinition
{
    public BlockTypeDescriptor Descriptor { get; } = descriptor;
    public Type PayloadType => typeof(T);
    public ContentEditorMetadata? Editor { get; } = editor;
    public string ImplementationDigest { get; } = BuildPipeline.Hash(Encoding.UTF8.GetBytes(string.Join("\n",
        new[] { typeof(T).Assembly, validate.Method.Module.Assembly }.Distinct().OrderBy(a => a.FullName, StringComparer.Ordinal)
            .Select(a => BuildPipeline.Hash(File.ReadAllBytes(a.Location))))));
    public object DecodeFields(Block block)
    {
        try { return block.Fields.Deserialize<T>(CompositionJson.Options) ?? throw new ContentValidationException("Block fields are required."); }
        catch (JsonException exception) { throw new ContentValidationException("Invalid registered Block fields: " + exception.Message); }
    }
    public void Validate(Block block, BlockValidationContext context)
    {
        if (Editor is not null) ContentEditorContract.ValidateValues(Editor, block.Fields, context.Website, context.Design);
        validate((T)DecodeFields(block), context);
    }
}
