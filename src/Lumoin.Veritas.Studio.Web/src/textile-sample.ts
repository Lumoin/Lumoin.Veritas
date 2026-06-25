// A cotton-to-garment supply-chain sample (Turtle) grounded in real vocabularies: UNTP (UN Transparency
// Protocol, UN/CEFACT) for the product passports / facilities / materials / traceability, and WRI Aqueduct
// 4.0 water-risk scores attached per HydroBASINS sub-basin. Five actors (farm -> gin -> spinner -> fabric
// mill with wet-processing -> garment maker, plus a brand), each product an untp:Product carrying fibre
// composition + recycled content + origin, downstream linked to upstream inputs via vu:upstreamInput.
// UNTP base IRIs are used as published; draft/unpublished terms (cottonSpecies, PartyRole codes) and the
// water-risk vocabulary (no official IRI exists) are minted under the local vu:/vw:/aq: namespaces.
export const TEXTILE_SAMPLE = `@prefix rdf:    <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
@prefix rdfs:   <http://www.w3.org/2000/01/rdf-schema#> .
@prefix xsd:    <http://www.w3.org/2001/XMLSchema#> .
@prefix schema: <https://schema.org/> .
@prefix untp:   <https://vocabulary.uncefact.org/untp/> .
@prefix country: <https://vocabulary.uncefact.org/CountryId#> .
@prefix vu:     <https://veritas.app/ns/untp#> .
@prefix vw:     <https://veritas.app/ns/water#> .
@prefix aq:     <https://veritas.app/ns/water/aqueduct40/indicator/> .
@prefix ex:     <https://veritas.app/data/cotton-chain/> .

# Hydrological basins (HydroBASINS Level-6 sub-basins; 6-digit code as the basin key).
ex:basin-261034 a vw:HydroBasin ;
    rdfs:label "Indus sub-basin 261034" ;
    vw:hydroBasinsLevel6Code "261034" .

ex:basin-352007 a vw:HydroBasin ;
    rdfs:label "Bhairab-Karnaphuli sub-basin 352007" ;
    vw:hydroBasinsLevel6Code "352007" .

ex:basin-118044 a vw:HydroBasin ;
    rdfs:label "Lower Mekong sub-basin 118044" ;
    vw:hydroBasinsLevel6Code "118044" .

# Water-risk indicator scores (WRI Aqueduct 4.0). Each score node links a basin, an
# indicator, the raw value, the 0-5 score, the integer category bin and the text label.
ex:wr-261034-bws a vw:WaterRiskScore ;
    vw:assessedBasin ex:basin-261034 ;
    vw:indicator aq:bws ;
    vw:datasetVersion "Aqueduct 4.0" ;
    vw:rawValue "0.91"^^xsd:decimal ;
    vw:score "4.6"^^xsd:decimal ;
    vw:categoryBin "5"^^xsd:integer ;
    vw:label "Extremely High (>80%)" .

ex:wr-352007-ucw a vw:WaterRiskScore ;
    vw:assessedBasin ex:basin-352007 ;
    vw:indicator aq:ucw ;
    vw:datasetVersion "Aqueduct 4.0" ;
    vw:rawValue "0.88"^^xsd:decimal ;
    vw:score "4.4"^^xsd:decimal ;
    vw:categoryBin "5"^^xsd:integer ;
    vw:label "Extremely High" .

ex:wr-118044-bws a vw:WaterRiskScore ;
    vw:assessedBasin ex:basin-118044 ;
    vw:indicator aq:bws ;
    vw:datasetVersion "Aqueduct 4.0" ;
    vw:rawValue "0.14"^^xsd:decimal ;
    vw:score "1.4"^^xsd:decimal ;
    vw:categoryBin "2"^^xsd:integer ;
    vw:label "Low-Medium (10-20%)" .

# Facilities (UNTP Facility; subject of a Digital Facility Record). Water-relevant
# facilities (farm, gin, spinner, dye house, garment plant) carry a locatedInBasin link.
ex:farm-jhang a untp:Facility ;
    schema:name "Jhang Cotton Growers Cooperative" ;
    untp:processCategory "growing" ;
    untp:locationInformation country:PK ;
    vw:locatedInBasin ex:basin-261034 .

ex:gin-multan a untp:Facility ;
    schema:name "Multan Ginning Works" ;
    untp:processCategory "ginning" ;
    untp:locationInformation country:PK ;
    vw:locatedInBasin ex:basin-261034 .

ex:spinner-faisalabad a untp:Facility ;
    schema:name "Faisalabad Spinning Mill" ;
    untp:processCategory "spinning" ;
    untp:locationInformation country:PK ;
    vw:locatedInBasin ex:basin-261034 .

ex:mill-chittagong a untp:Facility ;
    schema:name "Chittagong Fabric & Dye House" ;
    untp:processCategory "wet-processing" ;
    untp:locationInformation country:BD ;
    vw:locatedInBasin ex:basin-352007 .

ex:garment-dhaka a untp:Facility ;
    schema:name "Dhaka Apparel CMT" ;
    untp:processCategory "garment-manufacturing" ;
    untp:locationInformation country:BD ;
    vw:locatedInBasin ex:basin-352007 .

# Party (UNTP Party) with a PartyRole code.
ex:party-brand a untp:Party ;
    schema:name "Northwind Apparel" ;
    untp:relatedParty untp:PartyRole-brandOwner .

# Products. Each is a UNTP Product carried by a Digital Product Passport, with a
# materialProvenance Material (fibre composition + recycled fraction + origin) and a
# producedAtFacility link. Downstream products reference upstream inputs via
# vu:upstreamInput (event-based traceability chaining garment -> fabric -> yarn -> lint -> seed cotton).
ex:seedcotton-lot-001 a untp:Product ;
    schema:name "Seed cotton lot SC-001" ;
    untp:productCategory "5201" ;
    untp:cottonSpecies "Gossypium hirsutum" ;
    untp:producedAtFacility ex:farm-jhang ;
    untp:countryOfProduction country:PK ;
    untp:materialProvenance ex:mat-seedcotton-001 .

ex:mat-seedcotton-001 a untp:Material ;
    untp:materialType "cotton" ;
    untp:massFraction "1.0"^^xsd:decimal ;
    untp:recycledMassFraction "0.0"^^xsd:decimal ;
    untp:originCountry country:PK .

ex:lint-bale-001 a untp:Product ;
    schema:name "Cotton lint bale LB-001" ;
    untp:productCategory "5201" ;
    untp:producedAtFacility ex:gin-multan ;
    untp:countryOfProduction country:PK ;
    untp:materialProvenance ex:mat-lint-001 ;
    vu:upstreamInput ex:seedcotton-lot-001 .

ex:mat-lint-001 a untp:Material ;
    untp:materialType "cotton" ;
    untp:massFraction "1.0"^^xsd:decimal ;
    untp:recycledMassFraction "0.0"^^xsd:decimal ;
    untp:originCountry country:PK ;
    untp:mass "227.0"^^xsd:decimal .

ex:yarn-lot-001 a untp:Product ;
    schema:name "Combed cotton yarn YN-001" ;
    untp:productCategory "5205" ;
    untp:producedAtFacility ex:spinner-faisalabad ;
    untp:countryOfProduction country:PK ;
    untp:materialProvenance ex:mat-yarn-001 ;
    vu:upstreamInput ex:lint-bale-001 .

ex:mat-yarn-001 a untp:Material ;
    untp:materialType "cotton" ;
    untp:massFraction "1.0"^^xsd:decimal ;
    untp:recycledMassFraction "0.0"^^xsd:decimal ;
    untp:originCountry country:PK .

ex:fabric-roll-001 a untp:Product ;
    schema:name "Dyed woven fabric FB-001" ;
    untp:productCategory "5208" ;
    untp:producedAtFacility ex:mill-chittagong ;
    untp:countryOfProduction country:BD ;
    untp:materialProvenance ex:mat-fabric-001 ;
    vu:upstreamInput ex:yarn-lot-001 .

ex:mat-fabric-001 a untp:Material ;
    untp:materialType "cotton" ;
    untp:massFraction "0.95"^^xsd:decimal ;
    untp:recycledMassFraction "0.30"^^xsd:decimal ;
    untp:originCountry country:PK .

ex:garment-sku-001 a untp:Product ;
    schema:name "Men's woven shirt GR-001" ;
    untp:productCategory "6205" ;
    untp:producedAtFacility ex:garment-dhaka ;
    untp:countryOfProduction country:BD ;
    untp:relatedParty ex:party-brand ;
    untp:materialProvenance ex:mat-garment-001 ;
    vu:upstreamInput ex:fabric-roll-001 .

ex:mat-garment-001 a untp:Material ;
    untp:materialType "cotton" ;
    untp:massFraction "0.95"^^xsd:decimal ;
    untp:recycledMassFraction "0.30"^^xsd:decimal ;
    untp:originCountry country:PK .
`;

/** Traces garments transitively (vu:upstreamInput+) to upstream facilities in high-water-stress basins. */
export const TEXTILE_QUERY = `PREFIX xsd:    <http://www.w3.org/2001/XMLSchema#>
PREFIX schema: <https://schema.org/>
PREFIX untp:   <https://vocabulary.uncefact.org/untp/>
PREFIX vu:     <https://veritas.app/ns/untp#>
PREFIX vw:     <https://veritas.app/ns/water#>

# Which garments trace (transitively, through the make/transform chain) to an upstream
# facility sitting in a high-water-stress basin? Surface the garment, the offending
# upstream product and facility, the basin and its water-risk score.
SELECT ?garmentName ?facilityName ?basinCode ?riskScore ?riskLabel
WHERE {
  ?garment a untp:Product ;
           schema:name ?garmentName ;
           untp:productCategory "6205" .

  # Walk the traceability chain upstream over one-or-more vu:upstreamInput hops.
  ?garment vu:upstreamInput+ ?upstreamProduct .

  ?upstreamProduct untp:producedAtFacility ?facility .
  ?facility schema:name ?facilityName ;
            vw:locatedInBasin ?basin .
  ?basin vw:hydroBasinsLevel6Code ?basinCode .

  # That basin carries a water-risk score; keep only High / Extremely High (score >= 4).
  ?risk vw:assessedBasin ?basin ;
        vw:score ?riskScore ;
        vw:label ?riskLabel .
  FILTER (?riskScore >= 4.0)
}
ORDER BY DESC(?riskScore) ?facilityName`;
