/* Independent C ABI probe for Siemens uf_cambnd.h and uf_param.h.
 * This is a test library, not an NX runtime or a shipped journal dependency. */
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef enum {
    UF_PARAM_DEFAULT, UF_PARAM_INHERITED, UF_PARAM_OVERRIDDEN, UF_PARAM_INVALID_INDEX
} UF_PARAM_status_t;
_Static_assert(sizeof(UF_PARAM_status_t) == sizeof(int), "UF parameter status uses C int storage");
static struct {
    uint32_t tag, definer;
    int index, state, error, definer_error;
} parameter_statuses[32];
static int parameter_count;

void fixture_param_status(uint32_t tag, int index, int state, int error) {
    for (int n = 0; n < parameter_count; ++n) {
        if (parameter_statuses[n].tag == tag && parameter_statuses[n].index == index) {
            parameter_statuses[n].state = state;
            parameter_statuses[n].error = error;
            return;
        }
    }
    if (parameter_count >= 32) abort();
    parameter_statuses[parameter_count].tag = tag;
    parameter_statuses[parameter_count].definer = 0;
    parameter_statuses[parameter_count].definer_error = 0;
    parameter_statuses[parameter_count].index = index;
    parameter_statuses[parameter_count].state = state;
    parameter_statuses[parameter_count++].error = error;
}

void fixture_param_definer(uint32_t tag, int index, uint32_t definer, int error) {
    for (int n = 0; n < parameter_count; ++n) {
        if (parameter_statuses[n].tag == tag && parameter_statuses[n].index == index) {
            parameter_statuses[n].definer = definer;
            parameter_statuses[n].definer_error = error;
            return;
        }
    }
    abort();
}

int UF_PARAM_ask_param_definer(uint32_t tag, int index, uint32_t *definer) {
    for (int n = 0; n < parameter_count; ++n) {
        if (parameter_statuses[n].tag == tag && parameter_statuses[n].index == index) {
            if (parameter_statuses[n].definer_error) return parameter_statuses[n].definer_error;
            *definer = parameter_statuses[n].definer;
            return 0;
        }
    }
    return 90005;
}

int UF_PARAM_ask_param_status(uint32_t tag, int index, UF_PARAM_status_t *status) {
    for (int n = 0; n < parameter_count; ++n) {
        if (parameter_statuses[n].tag == tag && parameter_statuses[n].index == index) {
            if (parameter_statuses[n].error) return parameter_statuses[n].error;
            /* -1 simulates a faulty successful call that left output unwritten. */
            if (parameter_statuses[n].state != -1)
                *status = (UF_PARAM_status_t)parameter_statuses[n].state;
            return 0;
        }
    }
    return 90005;
}

typedef enum { boundary_closed, boundary_open } UF_CAM_boundary_type_t;
typedef enum { material_undefined, material_in_left, material_out_right } UF_CAM_material_side_t;
typedef void *UF_CAMBND_boundary_t;
typedef void *UF_CAMBND_item_t;
typedef struct UF_CAMBND_app_data_s UF_CAMBND_app_data_t;
typedef struct {
    UF_CAM_boundary_type_t boundary_type;
    int plane_type;
    double origin[3];
    double matrix[9];
    UF_CAM_material_side_t material_side;
    int ignore_holes, ignore_islands, ignore_chamfers;
    UF_CAMBND_app_data_t *app_data;
} UF_CAMBND_boundary_data_t;

static int boundary_token, member_token;
static void *allocations[8];
static int plane_mode = 1, error_code, free_count, invalid_frees, data_calls;
static int write_error, write_calls;
static double plane_origin[3], plane_matrix[9];

void fixture_reset(int mode, int error) {
    parameter_count = 0;
    plane_mode = mode;
    error_code = error;
    free_count = invalid_frees = data_calls = 0;
    write_error = write_calls = 0;
    for (int i = 0; i < 3; ++i) plane_origin[i] = 100.0 + i;
    for (int i = 0; i < 9; ++i) plane_matrix[i] = mode == 0 ? (i % 4 == 0 ? 1.0 : 0.0) : 200.0 + i;
}

void fixture_write_error(int error) { write_error = error; }

size_t fixture_layout(int index) {
    const size_t values[] = {
        sizeof(UF_CAMBND_boundary_data_t),
        offsetof(UF_CAMBND_boundary_data_t, boundary_type),
        offsetof(UF_CAMBND_boundary_data_t, plane_type),
        offsetof(UF_CAMBND_boundary_data_t, origin),
        offsetof(UF_CAMBND_boundary_data_t, matrix),
        offsetof(UF_CAMBND_boundary_data_t, material_side),
        offsetof(UF_CAMBND_boundary_data_t, ignore_holes),
        offsetof(UF_CAMBND_boundary_data_t, ignore_islands),
        offsetof(UF_CAMBND_boundary_data_t, ignore_chamfers),
        offsetof(UF_CAMBND_boundary_data_t, app_data)
    };
    return index >= 0 && index < 10 ? values[index] : (size_t)-1;
}

int fixture_stat(int index) {
    int outstanding = 0;
    for (int i = 0; i < 8; ++i) outstanding += allocations[i] != NULL;
    return index == 0 ? free_count : index == 1 ? invalid_frees : index == 2 ? outstanding : index == 3 ? data_calls : write_calls;
}

static int one_handle(void *handle, int *count, void ***out) {
    *count = 0;
    *out = NULL;
    for (int i = 0; i < 8; ++i) {
        if (!allocations[i]) {
            void **array = (void **)malloc(sizeof(void *));
            if (!array) return 90001;
            array[0] = handle;
            allocations[i] = array;
            *count = 1;
            *out = array;
            return 0;
        }
    }
    return 90002;
}

void UF_free(void *array) {
    for (int i = 0; i < 8; ++i) {
        if (array && allocations[i] == array) {
            allocations[i] = NULL;
            ++free_count;
            free(array);
            return;
        }
    }
    ++invalid_frees;
}

/* uf_defs.h: C logical is unsigned char, C++ logical is bool (one byte).
 * uf_so.h also returns an allocated array of 32-bit tags. */
int UF_SO_is_so(uint32_t tag, unsigned char *is_so) {
    *is_so = tag == 1 ? 0 : 1;
    return 0;
}

int UF_SO_ask_update_error_code(uint32_t tag, int *error) {
    *error = tag == 2 ? 777 : 0;
    return 0;
}

int UF_SO_ask_parents(uint32_t tag, int options, int *count, uint32_t **parents) {
    if (options != 8) return 90006;
    *count = 0;
    *parents = NULL;
    if (tag == 3) return 0;
    for (int i = 0; i < 8; ++i) {
        if (!allocations[i]) {
            uint32_t *array = malloc(3 * sizeof(uint32_t));
            if (!array) return 90001;
            array[0] = UINT32_C(0xf0000001);
            array[1] = 29;
            array[2] = UINT32_C(0xf0000003);
            allocations[i] = array;
            *count = 3;
            *parents = array;
            return tag == 5 ? 90007 : 0;
        }
    }
    return 90002;
}

int UF_CAMBND_ask_boundaries(uint32_t object_tag, int kind, int *count, UF_CAMBND_boundary_t **out) {
    (void)object_tag;
    *count = 0;
    *out = NULL;
    return kind == 1 ? one_handle(&boundary_token, count, out) : 0;
}

int UF_CAMBND_ask_boundary_data(UF_CAMBND_boundary_t boundary, UF_CAMBND_boundary_data_t *data) {
    if (boundary != &boundary_token) return 90003;
    ++data_calls;
    if (error_code) return error_code;
    memset(data, 0, sizeof(*data));
    data->boundary_type = boundary_open;
    data->plane_type = plane_mode;
    memcpy(data->origin, plane_origin, sizeof(plane_origin));
    memcpy(data->matrix, plane_matrix, sizeof(plane_matrix));
    data->material_side = material_out_right;
    data->ignore_holes = data->ignore_chamfers = 1;
    data->app_data = NULL; /* The documented read excludes application data. */
    return 0;
}

int UF_CAMBND_set_boundary_plane(UF_CAMBND_boundary_t boundary, double origin[3], double matrix[9]) {
    if (boundary != &boundary_token || plane_mode != 0) return 90003;
    if (write_error) return write_error;
    ++write_calls;
    memcpy(plane_origin, origin, sizeof(plane_origin));
    memcpy(plane_matrix, matrix, sizeof(plane_matrix));
    return 0;
}

int UF_MODL_update(void) { return 0; }

int UF_CAMBND_ask_boundary_items(UF_CAMBND_boundary_t boundary, int *count, UF_CAMBND_item_t **out) {
    if (boundary != &boundary_token) return 90003;
    return one_handle(&member_token, count, out);
}

int UF_CAMBND_ask_item_entity(UF_CAMBND_item_t item, uint32_t *entity) {
    if (item != &member_token) return 90004;
    *entity = UINT32_C(0xf0000042);
    return 0;
}
