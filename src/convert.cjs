const { readFileSync, writeFileSync } = require('node:fs');
const { gzipSync } = require('node:zlib');
const loadOcct = require('./occt/occt-import-js.js');

// Limit the size of each WPF mesh while keeping all vertices shared within a chunk.
const trianglesPerChunk = 12000;

function encodeChunk(mesh, start, end, hasNormals) {
    const sourceIndices = mesh.index.array;
    const vertexMap = new Map();
    const sourceVertices = [];
    const indices = Buffer.allocUnsafe(4 * (end - start));
    for (let i = start; i < end; i++) {
        const sourceVertex = sourceIndices[i];
        if (!vertexMap.has(sourceVertex)) {
            vertexMap.set(sourceVertex, sourceVertices.length);
            sourceVertices.push(sourceVertex);
        }
        indices.writeUInt32LE(vertexMap.get(sourceVertex), 4 * (i - start));
    }

    const count = sourceVertices.length;
    const positions = Buffer.allocUnsafe(12 * count);
    const normals = hasNormals ? Buffer.allocUnsafe(12 * count) : Buffer.alloc(0);
    const sourcePositions = mesh.attributes.position.array;
    const sourceNormals = hasNormals ? mesh.attributes.normal.array : null;
    for (const [vertex, sourceVertex] of sourceVertices.entries()) {
        for (let axis = 0; axis < 3; axis++) {
            const coordinate = sourcePositions[3 * sourceVertex + axis];
            if (!Number.isFinite(coordinate)) throw new Error('Invalid vertex coordinate');
            positions.writeFloatLE(coordinate, 12 * vertex + 4 * axis);
            if (hasNormals) normals.writeFloatLE(sourceNormals[3 * sourceVertex + axis], 12 * vertex + 4 * axis);
        }
    }

    const header = Buffer.alloc(8);
    header.writeUInt32LE(count, 0);
    header.writeUInt32LE(end - start, 4);
    return Buffer.concat([header, positions, normals, indices]);
}

function hasLongVerticalAxis(meshes) {
    const minimum = [Infinity, Infinity, Infinity];
    const maximum = [-Infinity, -Infinity, -Infinity];
    for (const mesh of meshes) {
        const positions = mesh.attributes.position.array;
        for (let index = 0; index < positions.length; index += 3) {
            for (let axis = 0; axis < 3; axis++) {
                minimum[axis] = Math.min(minimum[axis], positions[index + axis]);
                maximum[axis] = Math.max(maximum[axis], positions[index + axis]);
            }
        }
    }
    const spans = maximum.map((end, axis) => end - minimum[axis]);
    return spans[2] > 5 * Math.max(spans[0], spans[1]);
}

function convert(occt, filename, output) {
    const result = occt.ReadStepFile(readFileSync(filename), {
        linearUnit: 'millimeter',
        linearDeflectionType: 'absolute_value',
        linearDeflection: 0.10,
        angularDeflection: 0.24,
    });
    if (!result.success) throw new Error('OCCT could not read the STEP file');
    const meshes = result.meshes.filter((mesh) => mesh.index.array.length > 0);
    if (!meshes.length) throw new Error('No renderable parts');

    const header = Buffer.alloc(12);
    header.write('M511');
    header.writeUInt32LE(meshes.length, 4);
    header.writeUInt32LE(hasLongVerticalAxis(meshes) ? 1 : 0, 8);
    const parts = [header];
    for (const mesh of meshes) {
        const indexCount = mesh.index.array.length;
        const chunkSize = 3 * trianglesPerChunk;
        const hasNormals = mesh.attributes.normal?.array.length === mesh.attributes.position.array.length;
        const color = mesh.color;
        const hasColor = Array.isArray(color) && color.length === 3 && color.every(Number.isFinite);
        const partHeader = Buffer.alloc(8);
        partHeader.writeUInt32LE(Math.ceil(indexCount / chunkSize));
        partHeader[7] = (hasNormals ? 1 : 0) | (hasColor ? 2 : 0);
        if (hasColor) color.forEach((value, i) => { partHeader[4 + i] = Math.round(Math.max(0, Math.min(1, value)) * 255); });
        parts.push(partHeader);
        for (let first = 0; first < indexCount; first += chunkSize) {
            parts.push(encodeChunk(mesh, first, Math.min(first + chunkSize, indexCount), hasNormals));
        }
    }
    writeFileSync(output, gzipSync(Buffer.concat(parts), { level: 1 }));
}

async function main() {
    const [command, input, output] = process.argv.slice(2);
    if (command !== '--import' || !input || !output) throw new Error('Pass --import INPUT OUTPUT');
    convert(await loadOcct(), input, output);
}

main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
});
