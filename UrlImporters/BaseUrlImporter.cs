using ImGuiNET;
using PassiveSkillTreePlanter.UrlDecoders;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExileCore.Shared.Helpers;
using SharpDX;
using System;

namespace PassiveSkillTreePlanter.UrlImporters;

public abstract class BaseUrlImporter
{
    private int _selectedProgress = -1;
    private int _selectedVariant;
    private CancellationTokenSource _dataFetchCancellation;
    private Task<List<FetchedTree>> _dataFetchTask;
    private string _urlInput = "";

    protected record FetchedTree(List<ushort> Passives, string Name, string Url, ESkillTreeType TreeType, bool CanSelectProgress);

    protected abstract Task<List<FetchedTree>> FetchTrees(string url, CancellationToken cancellationToken);
    protected abstract bool IsValidUrl(string url);
    protected abstract string Name { get; }
    protected virtual uint UrlMaxLength => 200;

    public TreeConfig.Tree DrawAddInterface()
    {
        if (!ImGui.TreeNode($"Import {Name} build"))
        {
            return null;
        }

        if (ImGui.InputTextWithHint("##input", "Url", ref _urlInput, UrlMaxLength))
        {
            _selectedProgress = -1;
            _selectedVariant = 0;
            _dataFetchCancellation?.Cancel();
            _dataFetchCancellation = null;
            _dataFetchTask = null;
            if (IsValidUrl(_urlInput))
            {
                _dataFetchCancellation = new CancellationTokenSource();
                _dataFetchTask = FetchTrees(_urlInput, _dataFetchCancellation.Token);
            }
        }

        if (!string.IsNullOrWhiteSpace(_urlInput) && !IsValidUrl(_urlInput))
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), $"Not a valid {Name} url");
        }

        if (_dataFetchTask is { IsCompletedSuccessfully: true })
        {
            var data = _dataFetchTask.Result;
            if (data?.Count is 0 or null)
            {
                ImGui.TextColored(Color.Red.ToImguiVec4(), "No variants in the requested build");
            }
            else
            {
                var validTreeName = !string.IsNullOrEmpty(data[_selectedVariant]?.Name);

                if (data.Count != 1)
                {
                    if (ImGui.SliderInt(
                            "Tree",
                            ref _selectedVariant,
                            0,
                            data.Count - 1,
                            validTreeName ? data[_selectedVariant].Name : null,
                            ImGuiSliderFlags.AlwaysClamp
                        ))
                    {
                        _selectedProgress = data[_selectedVariant]?.Passives?.Count ?? 0;
                    }
                }
                else
                {
                    _selectedVariant = 0;
                }

                if (_selectedProgress == -1)
                {
                    _selectedProgress = data[_selectedVariant]?.Passives?.Count ?? 0;
                }

                if (data[_selectedVariant]?.Passives == null)
                {
                    ImGui.TextColored(Color.Red.ToImguiVec4(), "Selected variant does not contain valid build data");
                }
                else
                {
                    if (data[_selectedVariant].CanSelectProgress)
                    {
                        ImGui.SliderInt("Progress", ref _selectedProgress, 0, data[_selectedVariant].Passives.Count, null, ImGuiSliderFlags.AlwaysClamp);
                    }

                    if (ImGui.Button("Import"))
                    {
                        ImGui.TreePop();

                        var tree = new TreeConfig.Tree
                        {
											   
                            Tag = validTreeName ? $"{data[_selectedVariant].Name}, {_selectedProgress} pts"
                                : $"{Name} import ({data[_selectedVariant].Url}), {_selectedProgress} pts",
                            SkillTreeUrl = PathOfExileUrlDecoder.Encode(
                                data[_selectedVariant].Passives.Take(_selectedProgress).ToHashSet(),
                                data[_selectedVariant].TreeType
                            )
                        };

                        if (data.Count != 1)
                        {
                            //select next tree after import
                            _selectedVariant = Math.Min(_selectedVariant + 1, data.Count - 1);
                            _selectedProgress = data[_selectedVariant]?.Passives?.Count ?? 0;
                        }

                        return tree;
                    }
                }
            }
        }
        else if (_dataFetchTask is { IsCompleted: false })
        {
            ImGui.Text("Loading the build data...");
        }
        else if (_dataFetchTask is { IsCompleted: true, IsCompletedSuccessfully: false })
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), $"Data fetch failed: {_dataFetchTask.Exception}");
        }

        ImGui.TreePop();
        return null;
    }
}