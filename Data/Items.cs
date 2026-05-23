using System;

// Awful name but it's consistent with SpeciesPhaseResource and NuclideResource
// Things that can be obtained
// The base ItemResource has an implicit count of 1 and can be extended to have unique properties while maintaining item handling code
// Name is effectively a key. Ensure you do not have "Potato Seed", "PotatoSeed", and "potato seed" in the code
// My convention: Title Case
public class ItemResource
{
    public string Name;
    public double Mass; // kg, will add to the mass of whatever is holding this
    public double Volume; // m^3, the holder needs at least this much free volume to accept this
}

// StackableItemResource should not have any unique properties
// If "Potato Seed" has different genetics from "Potato Seed", don't use a StackableItemResource
public class StackableItemResource : ItemResource
{
    public uint Count;
    // All code that manipulates stacks and changes Count *must* adjust Mass and Volume
    // This is probably going to cause problems in the future but I'm too lazy to make GetMass and GetVolume functions that calculate based on Count
}
