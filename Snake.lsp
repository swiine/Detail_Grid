(vl-load-com)

(setq *SNK:BlockFolder* "A:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Scripts\\Snake\\_Support\\")

(setq *SNK:Frames*
  '(("A1" "TTW_stdCountry_Block_AnnoFrame_A1L" "TTW_FrameBlock_A1.dwg" "TTW_stdCountry_Frame_A1")
    ("A0" "TTW_stdCountry_Block_AnnoFrame_A1V" "TTW_FrameBlock_A0.dwg" "TTW_stdCountry_Frame_A1")
   )
)

(setq *SNK:ScalePrefix* "ttw_scale_1:")
(setq *SNK:ScaleSuffix* "m")

(if (not *SNK:LastSize*)  (setq *SNK:LastSize* "A1"))
(if (not *SNK:LastScale*) (setq *SNK:LastScale* 50.0))

(defun SNK:num->str (n / s)
  (setq s (rtos n 2 4))
  (if (vl-string-search "." s)
    (setq s (vl-string-right-trim "." (vl-string-right-trim "0" s)))
  )
  s
)

(defun SNK:scale-name (n)
  (strcat *SNK:ScalePrefix* (SNK:num->str n) *SNK:ScaleSuffix*)
)

(defun SNK:drawing-scales (/ dict name lst pre suf)
  (setq pre (strcase *SNK:ScalePrefix*)
        suf (strcase *SNK:ScaleSuffix*)
  )
  (if (setq dict (dictsearch (namedobjdict) "ACAD_SCALELIST"))
    (foreach pair dict
      (if (= (car pair) 350)
        (progn
          (setq name (strcase (cdr (assoc 300 (entget (cdr pair))))))
          (if (wcmatch name (strcat pre "*" suf))
            (setq lst (cons (distof (substr name (1+ (strlen pre))
                                            (- (strlen name) (strlen pre) (strlen suf))))
                            lst))
          )
        )
      )
    )
  )
  (vl-sort (vl-remove nil lst) '<)
)

(defun SNK:ensure-scale (n)
  (if (not (member n (SNK:drawing-scales)))
    (progn
      (command "_.-SCALELISTEDIT" "_A" (SNK:scale-name n)
               (strcat "1:" (SNK:num->str (/ n 1000.0))) "_E")
      (princ (strcat "\nAdded scale " (SNK:scale-name n) " to the drawing."))
    )
  )
  (member n (SNK:drawing-scales))
)

(defun SNK:get-size (/ k)
  (initget (apply 'strcat (mapcar '(lambda (f) (strcat (car f) " ")) *SNK:Frames*)))
  (setq k (getkword (strcat "\nFrame size ["
                            (apply 'strcat (cdr (apply 'append
                              (mapcar '(lambda (f) (list "/" (car f))) *SNK:Frames*))))
                            "] <" *SNK:LastSize* ">: ")))
  (setq *SNK:LastSize* (cond (k) (*SNK:LastSize*)))
)

(defun SNK:get-scale (/ avail str n pos)
  (setq avail (SNK:drawing-scales))
  (if avail
    (princ (strcat "\nScales in drawing: 1:"
                   (apply 'strcat (cdr (apply 'append
                     (mapcar '(lambda (x) (list ", 1:" (SNK:num->str x))) avail))))))
    (princ "\nNo ttw_scale scales found in this drawing; the one you enter will be added.")
  )
  (while (not n)
    (setq str (vl-string-trim " " (getstring (strcat "\nScale 1:<" (SNK:num->str *SNK:LastScale*) ">: "))))
    (cond
      ((= str "") (setq n *SNK:LastScale*))
      (t
       (if (setq pos (vl-string-search ":" str)) (setq str (substr str (+ pos 2))))
       (setq n (distof str 2))
       (cond
         ((not (and n (> n 0))) (setq n nil) (princ "\nEnter a scale like 50 or 1:50."))
         ((and avail (not (member n avail)))
          (initget "Yes No")
          (if (/= (getkword (strcat "\n" (SNK:scale-name n)
                                    " is not in the drawing. Add it? [Yes/No] <Yes>: "))
                  "No")
            n
            (setq n nil)
          )
         )
       )
      )
    )
  )
  (setq *SNK:LastScale* n)
)

(defun SNK:extents (obj / mn mx)
  (vla-getboundingbox obj 'mn 'mx)
  (list (vlax-safearray->list mn) (vlax-safearray->list mx))
)

(defun SNK:midpoint (ext)
  (mapcar '(lambda (a b) (/ (+ a b) 2.0)) (car ext) (cadr ext))
)

(defun SNK:is-polyline (ent)
  (member (cdr (assoc 0 (entget ent))) '("LWPOLYLINE" "POLYLINE"))
)

(defun SNK:get-polyline (/ ent sel last)
  (while (not ent)
    (setvar "ERRNO" 0)
    (initget "Draw")
    (setq sel (entsel "\nSelect polyline for frame or [Draw] <Draw>: "))
    (if (and (null sel) (= (getvar "ERRNO") 52)) (setq sel "Draw"))
    (cond
      ((= sel "Draw")
       (setq last (entlast))
       (princ "\nDraw the polyline (Enter to finish, C to close):")
       (setvar "CMDECHO" 1)
       (command "_.PLINE")
       (while (> (logand (getvar "CMDACTIVE") 1) 0) (command pause))
       (setvar "CMDECHO" 0)
       (if (and (entlast) (not (equal (entlast) last)) (SNK:is-polyline (entlast)))
         (setq ent (entlast))
         (progn (princ "\nNo polyline was drawn.") (setq ent 'cancel))
       )
      )
      ((null sel) (princ "\nMissed, try again."))
      ((SNK:is-polyline (car sel)) (setq ent (car sel)))
      (t (princ "\nThat is not a polyline."))
    )
  )
  (if (/= ent 'cancel) ent)
)

(defun SNK:active-space (doc)
  (if (and (= (getvar "TILEMODE") 0) (= (getvar "CVPORT") 1))
    (vla-get-paperspace doc)
    (vla-get-modelspace doc)
  )
)

(defun SNK:ensure-block (doc frame / path tmp wrapper)
  (if (not (tblsearch "BLOCK" (cadr frame)))
    (if (setq path (findfile (if *SNK:BlockFolder*
                               (strcat *SNK:BlockFolder* (caddr frame))
                               (caddr frame))))
      (progn
        (setq tmp (vla-insertblock (vla-get-modelspace doc)
                    (vlax-3d-point '(0 0 0)) path 1 1 1 0)
              wrapper (vla-get-name tmp)
        )
        (vla-delete tmp)
        (vl-catch-all-apply 'vla-delete
          (list (vla-item (vla-get-blocks doc) wrapper)))
      )
      (princ (strcat "\nCannot find " (caddr frame)
                     ". Put it in a support path or set *SNK:BlockFolder*."))
    )
  )
  (tblsearch "BLOCK" (cadr frame))
)

(defun c:SNAKE (/ *error* doc size frame sc pl plExt plCen blk blkExt
                  blkSize plSize oldCmd oldOsm last)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if oldCmd (setvar "CMDECHO" oldCmd))
    (if oldOsm (setvar "OSMODE" oldOsm))
    (vla-endundomark doc)
    (if (not (wcmatch (strcase msg t) "*break*,*cancel*,*exit*"))
      (princ (strcat "\nError: " msg))
    )
    (princ)
  )

  (setq oldCmd (getvar "CMDECHO")
        oldOsm (getvar "OSMODE"))
  (setvar "CMDECHO" 0)
  (vla-startundomark doc)

  (setq size  (SNK:get-size)
        frame (assoc size *SNK:Frames*))
  (cond
    ((not (SNK:ensure-block doc frame)))
    ((not (setq sc (SNK:get-scale))))
    ((not (SNK:ensure-scale sc))
     (princ (strcat "\nCould not create scale " (SNK:scale-name sc) ".")))
    ((not (setq pl (SNK:get-polyline))))
    (t
     (setq plExt (SNK:extents (vlax-ename->vla-object pl))
           plCen (SNK:midpoint plExt))

     (setvar "CANNOSCALE" (SNK:scale-name sc))
     (setvar "OSMODE" 0)
     (setq last (entlast))
     (command "_.-INSERT" (cadr frame) "_S" 1 "_R" 0 (trans plCen 0 1))
     (while (> (logand (getvar "CMDACTIVE") 1) 0) (command ""))
     (setvar "OSMODE" oldOsm)

     (if (equal (entlast) last)
       (princ "\nThe frame was not inserted.")
       (progn
         (setq blk (vlax-ename->vla-object (entlast)))
         (if (and (cadddr frame) (tblsearch "LAYER" (cadddr frame)))
           (vla-put-layer blk (cadddr frame)))
         (setq blkExt (SNK:extents blk))
         (vla-move blk (vlax-3d-point (SNK:midpoint blkExt)) (vlax-3d-point plCen))

         (setq blkSize (mapcar '- (cadr blkExt) (car blkExt))
               plSize  (mapcar '- (cadr plExt) (car plExt)))
         (if (or (> (car plSize) (car blkSize)) (> (cadr plSize) (cadr blkSize)))
           (princ "\nWarning: the polyline is larger than the frame at this scale."))
         (princ (strcat "\n" size " frame placed at 1:" (SNK:num->str sc) "."))
       )
     )
    )
  )

  (setvar "CMDECHO" oldCmd)
  (vla-endundomark doc)
  (princ)
)

(princ "\nSnake loaded. Type SNAKE to run.")
(princ)
