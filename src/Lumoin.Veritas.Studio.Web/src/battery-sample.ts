// A small battery-passport sample dataset (Turtle) the in-browser engine loads so the shell answers real
// queries client-side — the "load your own set" of the local-first demo, standing in until a load-your-data
// control exists. Matches the editor's bat:/dpp: vocabulary; recycled-cobalt shares are integers.
export const BATTERY_SAMPLE = `@prefix bat: <https://veritas.app/ns/battery#> .
@prefix dpp: <https://veritas.app/ns/dpp#> .

bat:PackA_E12 a bat:Battery ; bat:cathode bat:NMC811 ; bat:recycledCobalt 11 ; dpp:passport "dpp-ABF1-91c" .
bat:PackA_E14 a bat:Battery ; bat:cathode bat:NMC811 ; bat:recycledCobalt 9 ; dpp:passport "dpp-7C2A-04d" .
bat:PackB_N03 a bat:Battery ; bat:cathode bat:NMC622 ; bat:recycledCobalt 14 ; dpp:passport "dpp-11E9-77a" .
bat:PackA_E09 a bat:Battery ; bat:cathode bat:NMC811 ; bat:recycledCobalt 7 ; dpp:passport "dpp-9F40-b21" .
bat:PackC_S21 a bat:Battery ; bat:cathode bat:LFP120 ; bat:recycledCobalt 0 ; dpp:passport "dpp-5571-3ce" .
`;

/** Lists the battery packs by recycled-cobalt share (descending). */
export const BATTERY_QUERY = `PREFIX bat: <https://veritas.app/ns/battery#>
SELECT ?battery ?cathode ?recycledCobalt WHERE {
  ?battery a bat:Battery ; bat:cathode ?cathode ; bat:recycledCobalt ?recycledCobalt
} ORDER BY DESC(?recycledCobalt)`;
