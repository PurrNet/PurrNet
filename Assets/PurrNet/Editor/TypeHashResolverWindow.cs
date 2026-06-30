using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PurrNet.Utils;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public class TypeHashResolverWindow : EditorWindow
    {
        private const string WindowTitle = "Type Hash Resolver";
        private const float RegisteredWidth = 80f;
        private const float HashWidth = 110f;
        private const float ButtonWidth = 62f;

        private readonly List<TypeMatch> _matches = new List<TypeMatch>();
        private Vector2 _scrollPosition;
        private string _hashInput = string.Empty;
        private string _errorMessage;
        private uint _resolvedHash;
        private bool _hasResolved;
        private double _lastScanMilliseconds;
        private int _assembliesScanned;
        private int _typesScanned;

        private struct TypeMatch
        {
            public Type Type;
            public uint Hash;
            public bool Registered;
        }

        [MenuItem("Tools/PurrNet/Analysis/Type Hash Resolver")]
        public static void ShowWindow()
        {
            var window = GetWindow<TypeHashResolverWindow>(WindowTitle);
            window.minSize = new Vector2(520, 320);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Resolve a PurrNet type hash by scanning all currently loaded assemblies. This also finds types that exist locally but are not registered with PurrNet.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _hashInput = EditorGUILayout.TextField("Hash ID", _hashInput);
            if (EditorGUI.EndChangeCheck())
                _hasResolved = false;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Resolve", GUILayout.Width(90)))
                Resolve();

            if (GUILayout.Button("Clear", GUILayout.Width(70)))
                Clear();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_errorMessage))
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);

            DrawResults();
        }

        private void DrawResults()
        {
            if (!_hasResolved)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                $"Hash: {_resolvedHash} (0x{_resolvedHash:X8})",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Matches: {_matches.Count} | Assemblies scanned: {_assembliesScanned} | Types scanned: {_typesScanned} | Scan: {_lastScanMilliseconds:0.##} ms",
                EditorStyles.miniLabel);

            if (_matches.Count == 0)
            {
                EditorGUILayout.HelpBox("No loaded type resolves to this hash.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(4);
            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = 0; i < _matches.Count; i++)
                DrawMatch(_matches[i]);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Registered", EditorStyles.toolbarButton, GUILayout.Width(RegisteredWidth));
            GUILayout.Label("Hash", EditorStyles.toolbarButton, GUILayout.Width(HashWidth));
            GUILayout.Label("Type", EditorStyles.toolbarButton);
            GUILayout.Space(ButtonWidth * 2f + 8f);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMatch(TypeMatch match)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(match.Registered ? "Yes" : "No", GUILayout.Width(RegisteredWidth));
            EditorGUILayout.LabelField($"0x{match.Hash:X8}", GUILayout.Width(HashWidth));
            EditorGUILayout.SelectableLabel(match.Type.FullName, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (GUILayout.Button("Copy", GUILayout.Width(ButtonWidth)))
                EditorGUIUtility.systemCopyBuffer = match.Type.FullName;

            if (GUILayout.Button("AQN", GUILayout.Width(ButtonWidth)))
                EditorGUIUtility.systemCopyBuffer = match.Type.AssemblyQualifiedName;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(match.Type.Assembly.GetName().Name, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void Resolve()
        {
            _matches.Clear();
            _errorMessage = null;
            _hasResolved = false;
            _assembliesScanned = 0;
            _typesScanned = 0;
            _lastScanMilliseconds = 0;

            if (!TryParseHash(_hashInput, out _resolvedHash, out _errorMessage))
                return;

            var start = EditorApplication.timeSinceStartup;
            ScanLoadedAssemblies(_resolvedHash);
            _lastScanMilliseconds = (EditorApplication.timeSinceStartup - start) * 1000d;

            _matches.Sort(CompareMatches);
            _hasResolved = true;
            _scrollPosition = Vector2.zero;
        }

        private void ScanLoadedAssemblies(uint hash)
        {
            var seenTypes = new HashSet<Type>();
            if (Hasher.TryGetType(hash, out var registeredType) && registeredType != null)
                AddMatch(registeredType, hash, true, seenTypes);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                _assembliesScanned++;

                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type?.FullName == null)
                        continue;

                    _typesScanned++;

                    uint candidateHash;
                    try
                    {
                        candidateHash = Hasher.Hash(type);
                    }
                    catch
                    {
                        continue;
                    }

                    if (candidateHash != hash)
                        continue;

                    AddMatch(type, candidateHash, Hasher.IsRegistered(type), seenTypes);
                }
            }
        }

        private void AddMatch(Type type, uint hash, bool registered, HashSet<Type> seenTypes)
        {
            if (!seenTypes.Add(type))
                return;

            _matches.Add(new TypeMatch
            {
                Type = type,
                Hash = hash,
                Registered = registered
            });
        }

        private static int CompareMatches(TypeMatch x, TypeMatch y)
        {
            int registered = y.Registered.CompareTo(x.Registered);
            if (registered != 0)
                return registered;

            int assembly = string.Compare(
                x.Type.Assembly.GetName().Name,
                y.Type.Assembly.GetName().Name,
                StringComparison.Ordinal);
            if (assembly != 0)
                return assembly;

            return string.Compare(x.Type.FullName, y.Type.FullName, StringComparison.Ordinal);
        }

        private static bool TryParseHash(string input, out uint hash, out string error)
        {
            hash = default;
            error = null;

            input = input?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a hash ID.";
                return false;
            }

            input = input.TrimEnd('u', 'U', 'l', 'L');

            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = input.Substring(2);
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash))
                    return true;

                error = "Hash ID is not a valid 32-bit hexadecimal value.";
                return false;
            }

            if (uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out hash))
                return true;

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedHash))
            {
                hash = unchecked((uint)signedHash);
                return true;
            }

            error = "Hash ID is not a valid 32-bit unsigned integer, signed integer, or 0x-prefixed hexadecimal value.";
            return false;
        }

        private void Clear()
        {
            _hashInput = string.Empty;
            _errorMessage = null;
            _matches.Clear();
            _hasResolved = false;
            _resolvedHash = default;
            _scrollPosition = Vector2.zero;
        }
    }
}
