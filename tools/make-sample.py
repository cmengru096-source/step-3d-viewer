"""Create a small original stepped block for the viewer's built-in demo."""

from math import sqrt
from pathlib import Path


def make_step():
    entities = []

    def add(value):
        entities.append(value)
        return f"#{len(entities)}"

    def refs(items):
        return f"({','.join(items)})"

    def coordinate(components):
        return "(" + ",".join(f"{value:.9f}" for value in components) + ")"

    def subtract(left, right):
        return tuple(a - b for a, b in zip(left, right))

    def direction(vector):
        magnitude = sqrt(sum(value * value for value in vector))
        return add(f"DIRECTION('',{coordinate(value / magnitude for value in vector)})")

    edges = {}

    def face(indices):
        oriented = []
        for a, b in zip(indices, indices[1:] + indices[:1]):
            low, high = sorted((a, b))
            if (low, high) not in edges:
                delta = subtract(vertices[high], vertices[low])
                magnitude = sqrt(sum(value * value for value in delta))
                vector = add(f"VECTOR('',{direction(delta)},{magnitude:.9f})")
                line = add(f"LINE('',{points[low]},{vector})")
                edges[low, high] = add(f"EDGE_CURVE('',{corners[low]},{corners[high]},{line},.T.)")
            sense = ".T." if a == low else ".F."
            oriented.append(add(f"ORIENTED_EDGE('',*,*,{edges[low, high]},{sense})"))
        loop = add(f"EDGE_LOOP('',{refs(oriented)})")
        boundary = add(f"FACE_OUTER_BOUND('',{loop},.T.)")
        p, q, r = (vertices[i] for i in indices[:3])
        u = subtract(q, p)
        v = subtract(r, p)
        normal = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        axes = add(f"AXIS2_PLACEMENT_3D('',{points[indices[0]]},{direction(normal)},{direction(u)})")
        plane = add(f"PLANE('',{axes})")
        return add(f"ADVANCED_FACE('',({boundary}),{plane},.T.)")

    context = add("APPLICATION_CONTEXT('automotive design')")
    add(f"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,{context})")
    product_context = add(f"PRODUCT_CONTEXT('',{context},'mechanical')")
    product = add(f"PRODUCT('DEMO','Stepped block','',({product_context}))")
    formation = add(f"PRODUCT_DEFINITION_FORMATION('1','',{product})")
    design_context = add(f"PRODUCT_DEFINITION_CONTEXT('part definition',{context},'design')")
    definition = add(f"PRODUCT_DEFINITION('design','',{formation},{design_context})")
    shape = add(f"PRODUCT_DEFINITION_SHAPE('','',{definition})")
    length = add("(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.))")
    angle = add("(NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.))")
    solid_angle = add("(NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT())")
    tolerance = add(f"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(0.001),{length},'distance_accuracy_value','')")
    geometry_context = add(
        f"(GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT(({tolerance})) "
        f"GLOBAL_UNIT_ASSIGNED_CONTEXT(({length},{angle},{solid_angle})) REPRESENTATION_CONTEXT('','3D'))"
    )
    footprint = [(0, 0), (80, 0), (80, 22), (30, 22), (30, 52), (0, 52)]
    vertices = [(x, y, z) for z in (0, 14) for x, y in footprint]
    points = [add(f"CARTESIAN_POINT('',{coordinate(vertex)})") for vertex in vertices]
    corners = [add(f"VERTEX_POINT('',{point})") for point in points]
    count = len(footprint)
    faces = [face(list(reversed(range(count)))), face(list(range(count, 2 * count)))]
    for i in range(count):
        j = (i + 1) % count
        faces.append(face([i, j, j + count, i + count]))
    shell = add(f"CLOSED_SHELL('',{refs(faces)})")
    body = add(f"MANIFOLD_SOLID_BREP('Stepped block',{shell})")
    origin = add(f"AXIS2_PLACEMENT_3D('',{points[0]},$,$)")
    representation = add(f"ADVANCED_BREP_SHAPE_REPRESENTATION('Stepped block',({origin},{body}),{geometry_context})")
    add(f"SHAPE_DEFINITION_REPRESENTATION({shape},{representation})")

    content = [
        "ISO-10303-21;",
        "HEADER;",
        "FILE_DESCRIPTION(('STEP 3D Viewer demo'),'2;1');",
        "FILE_NAME('stepped-block.step','2026-11-24T00:00:00',(''),(''),'','','');",
        "FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));",
        "ENDSEC;",
        "DATA;",
        *(f"#{number}={value};" for number, value in enumerate(entities, start=1)),
        "ENDSEC;",
        "END-ISO-10303-21;",
    ]
    return "\n".join(content) + "\n"


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    (root / "samples" / "stepped-block.step").write_text(make_step(), encoding="ascii", newline="\n")
