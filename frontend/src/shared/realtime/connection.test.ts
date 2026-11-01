import { hubUrlFrom } from './connection';

// Adresse du hub : même origine par défaut, adresse directe de l'API quand l'interface est servie ailleurs (Vercel).
describe('hubUrlFrom', () => {
  it('garde la même origine sans réglage', () => {
    expect(hubUrlFrom(undefined)).toBe('/hubs/live');
    expect(hubUrlFrom('  ')).toBe('/hubs/live');
  });

  it("prend l'adresse directe de l'API quand elle est donnée", () => {
    expect(hubUrlFrom(' https://hawala-api-preprod.up.railway.app/hubs/live ')).toBe('https://hawala-api-preprod.up.railway.app/hubs/live');
  });
});
