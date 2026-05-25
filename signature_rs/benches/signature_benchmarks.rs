use std::convert::TryFrom;

use criterion::{black_box, criterion_group, criterion_main, BenchmarkId, Criterion, Throughput};
use signature_rs::{compute_signature_3x3, compute_signatures, DEFAULT_SEED};

fn generate_test_data(width: usize, height: usize) -> Vec<u32> {
    let mut data = vec![0u32; width * height];
    let mut state = 42u32;

    for y in 0..height {
        for x in 0..width {
            let index = y * width + x;
            data[index] = if (x + y) % 4 == 0 {
                1 + ((x * y) % 10) as u32
            } else if (x * y) % 7 == 0 {
                state = state.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                1 + (state % 99)
            } else {
                ((x + (y * 2)) % 50) as u32
            };
        }
    }

    data
}

fn bench_single_signature(c: &mut Criterion) {
    let input = generate_test_data(3, 3);

    c.bench_function("compute_signature_3x3/3x3", |b| {
        b.iter(|| compute_signature_3x3(black_box(input.as_ptr()), black_box(DEFAULT_SEED)))
    });
}

fn bench_full_signature_pass(c: &mut Criterion) {
    let mut group = c.benchmark_group("compute_signatures");

    for (width, height) in [(3usize, 3usize), (16, 16), (34, 34), (64, 64)] {
        let input = generate_test_data(width, height);
        let mut output = vec![0u64; width * height];
        let width_i32 = i32::try_from(width).expect("benchmark width fits in i32");
        let height_i32 = i32::try_from(height).expect("benchmark height fits in i32");

        group.throughput(Throughput::Elements((width * height) as u64));
        group.bench_with_input(
            BenchmarkId::from_parameter(format!("{width}x{height}")),
            &(width, height),
            |b, _| {
                b.iter(|| {
                    compute_signatures(
                        black_box(input.as_ptr()),
                        black_box(output.as_mut_ptr()),
                        black_box(width_i32),
                        black_box(height_i32),
                        black_box(DEFAULT_SEED),
                    )
                })
            },
        );
    }

    group.finish();
}

criterion_group!(benches, bench_single_signature, bench_full_signature_pass);
criterion_main!(benches);
