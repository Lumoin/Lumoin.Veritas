using System;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The construct kinds ONE habitat probe row's own census signal may ride. The
/// survey census sets its two passed bits from a NAMED construct surface, so
/// this declaration is the datum that relates a row's admission columns — which
/// bit gates the row — to the census scan — which construct sets that bit. A
/// row declaring a carrier the census leaves both bits clear for is declaring a
/// dependency no admission column can express, which is what makes such a
/// dependency visible rather than latent.
/// </summary>
[Flags]
internal enum HabitatSignalCarriers
{
    /// <summary>The row's signal rides no declared construct kind.</summary>
    None = 0,

    /// <summary>Object number restrictions and the told functional and inverse-functional object-property characteristics — the constructs the survey census's counting mention covers.</summary>
    ObjectCounting = 1,

    /// <summary>Told one-ofs and object has-value restrictions — the constructs the survey census's nominal mention covers.</summary>
    Nominal = 2,

    /// <summary>Data cardinality restrictions. The census leaves both passed bits clear for them, so this carrier is one of the <see cref="CensusSilent"/> kinds.</summary>
    DataCounting = 4,

    /// <summary>Data existential, universal and has-value restrictions. The census leaves both passed bits clear for them, so this carrier is one of the <see cref="CensusSilent"/> kinds.</summary>
    DataValueRestriction = 8,

    /// <summary>The told functional data-property characteristic. The census leaves both passed bits clear for it, so this carrier is one of the <see cref="CensusSilent"/> kinds.</summary>
    DataCharacteristic = 16,

    /// <summary>Told inverse-role pairings. DECLARATION-ONLY: the survey scans an inverse mention but does not pass it to the walk, so a row declaring this carrier is declaring a dependency the two admission columns cannot express.</summary>
    Inverse = 32,

    /// <summary>The named composite of every carrier the survey census leaves both passed bits clear for: a row declaring any of them rides a signal no census bit reports.</summary>
    CensusSilent = DataCounting | DataValueRestriction | DataCharacteristic,
}
