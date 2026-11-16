/* Independent UF_EVAL contract fixture, not Siemens code or an NX emulator. */
#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>
#include <math.h>
#include <string.h>

typedef struct { unsigned char periodic; double limits[2], radius, center[3], x[3], y[3]; } Arc;
typedef struct { unsigned char periodic; double limits[2], minor, major, center[3], x[3], y[3]; } Ellipse;
typedef struct { unsigned char periodic, rational; int order, num_knots, num_control; } Spline;
_Static_assert(sizeof(Arc)==104, "native arc alignment");
_Static_assert(offsetof(Arc,limits)==8 && offsetof(Arc,y)==80, "arc field offsets");
_Static_assert(sizeof(Ellipse)==112 && offsetof(Ellipse,minor)==24, "ellipse alignment");
_Static_assert(sizeof(Spline)==16 && offsetof(Spline,order)==4, "spline logicals and integers");
static int kind, mode, live, bad_free, initialized, released, evaluated, arrays_freed, legacy_called;
static void *active, *arrays[2];
static uint32_t last_tag;

void fixture_reset(int curve_kind, int failure) {
    kind=curve_kind; mode=failure; live=bad_free=initialized=released=evaluated=arrays_freed=0;
    active=arrays[0]=arrays[1]=NULL; last_tag=0; legacy_called=0;
}
int fixture_stat(int field) {
    int values[]={live,bad_free,initialized,released,evaluated,arrays_freed,legacy_called};
    return field>=0 && field<7 ? values[field] : -1;
}
uint32_t fixture_tag(void) { return last_tag; }
int UF_EVAL_initialize(uint32_t tag, void **out) {
    (void)tag; (void)out; legacy_called++; return 690;
}
int UF_EVAL_initialize_2(uint32_t tag, void **out) {
    initialized++; last_tag=tag;
    if (mode==2) return 602;
    if (mode==3) return 0;
    active=malloc(8); *out=active; live++;
    return mode==1 ? 601 : 0;
}
int UF_EVAL_free(void *p) {
    if (!p || p!=active) {bad_free++; return 699;}
    free(p); active=NULL; live--; released++;
    return (mode==25 || mode==26) ? 625 : 0;
}
void UF_free(void *p) {
    for (int i=0;i<2;i++) if (p && p==arrays[i]) {
        free(p); arrays[i]=NULL; live--; arrays_freed++; return;
    }
    bad_free++;
}
static int flag(void *p, unsigned char *out, int value) {
    if (p!=active) return 698;
    if (mode==4) return 0;
    if (mode==6) return 606;
    *out=mode==5 ? 7 : (unsigned char)value;
    return 0;
}
int UF_EVAL_is_line(void *p, unsigned char *out) { return flag(p,out,kind==0 || kind==5); }
int UF_EVAL_is_arc(void *p, unsigned char *out) { return flag(p,out,kind==1 || kind==3 || kind==5); }
int UF_EVAL_is_ellipse(void *p, unsigned char *out) { return flag(p,out,kind==7); }
int UF_EVAL_is_spline(void *p, unsigned char *out) { return flag(p,out,kind==2 || kind==6); }
int UF_EVAL_is_periodic(void *p, unsigned char *out) { return flag(p,out,kind==3); }
static void limits(double *out) {
    out[0]=(kind==1 || kind==7) ? 0.2 : kind==3 ? -0.3 : kind==2 || kind==6 ? 2.3 : 2.;
    out[1]=(kind==1 || kind==7) ? 1.4 : kind==3 ? 2*acos(-1.)-0.3 : kind==2 || kind==6 ? 4.8 : 5.;
}
int UF_EVAL_ask_limits(void *p, double *out) {
    if (p!=active) return 698;
    if (mode==7) return 0;
    if (mode==10) return 610;
    limits(out);
    if (mode==8) out[0]=out[1]+1;
    if (mode==9) out[1]=NAN;
    if (mode==21) out[1]=7;
    return 0;
}
int UF_EVAL_ask_arc(void *p, Arc *out) {
    if (p!=active) return 698;
    if (mode==11) return 0;
    memset(out,0,sizeof(*out)); out->periodic=(unsigned char)(kind==3);
    limits(out->limits); out->radius=3.;
    out->center[0]=4.; out->center[1]=-2.; out->center[2]=5.;
    out->x[1]=1.; out->y[2]=1.;
    if (mode==12) out->radius=-1;
    if (mode==13) out->y[1]=1.;
    if (mode==14) out->periodic=99;
    return 0;
}
int UF_EVAL_ask_ellipse(void *p, Ellipse *out) {
    if (p!=active) return 698;
    memset(out,0,sizeof(*out)); limits(out->limits);
    out->minor=2.; out->major=5.; out->center[0]=4.; out->center[1]=-2.; out->center[2]=5.;
    out->x[1]=1.; out->y[2]=1.;
    if (mode==12) out->minor=-1;
    if (mode==13) out->major=1;
    return 0;
}
int UF_EVAL_ask_spline(void *p, Spline *out) {
    if (p!=active) return 698;
    if (mode==33) return 0;
    out->periodic=0; out->rational=(unsigned char)(kind==2);
    out->order=3; out->num_knots=6; out->num_control=3;
    if (mode==34) out->order=0;
    if (mode==35) {out->num_control=4;out->num_knots=7;}
    if (mode==36) out->rational=0;
    if (mode==37) out->periodic=1;
    return 0;
}
int UF_EVAL_ask_spline_control_pts(void *p, int *count, double **out) {
    if (p!=active) return 698;
    if (mode==16) {*count=3; return 0;}
    double w=kind==6 ? 1. : sqrt(0.5);
    double data[]={1,0,3,1, w,w,3*w,w, 0,1,3,1};
    double *a=malloc(sizeof(data)); memcpy(a,data,sizeof(data)); arrays[0]=a; live++;
    *out=a; *count=3;
    if (mode==17) *count=0;
    if (mode==18) a[6]=NAN;
    if (mode==19) a[7]=0;
    if (mode==29) *count=100000;
    return mode==28 ? 628 : 0;
}
int UF_EVAL_ask_spline_knots(void *p, int *count, double **out) {
    if (p!=active) return 698;
    double data[]={2,2,2,5,5,5};
    double *a=malloc(sizeof(data)); memcpy(a,data,sizeof(data)); arrays[1]=a; live++;
    *out=a; *count=6;
    if (mode==15) *count=-1;
    if (mode==20) a[2]=6;
    if (mode==30) *count=4;
    return mode==27 ? 627 : 0;
}
int UF_EVAL_evaluate(void *p, int derivatives, double u, double point[3], double unused[]) {
    (void)unused;
    if (p!=active || derivatives!=0) return 697;
    evaluated++;
    if (mode==23) return 0;
    if (mode==24 || mode==26) return 624;
    if (kind==0) {
        double t=(u-2)/3;
        point[0]=1+3*t; point[1]=2+6*t; point[2]=3+6*t;
    } else if (kind==1 || kind==3 || kind==7) {
        point[0]=4; point[1]=-2+(kind==7 ? 5 : 3)*cos(u); point[2]=5+(kind==7 ? 2 : 3)*sin(u);
    } else {
        /* Independent closed-form rational quadratic (no de Boor code). */
        double t=(u-2)/3, a=(1-t)*(1-t), b=2*t*(1-t), c=t*t;
        double w=kind==6 ? 1. : sqrt(0.5), divisor=a+b*w+c;
        point[0]=(a+b*w)/divisor; point[1]=(b*w+c)/divisor; point[2]=3.;
    }
    if (mode==22) point[0]+=0.1;
    if (mode==31 && u>2.5 && u<4.5) point[2]+=0.2;
    return 0;
}
